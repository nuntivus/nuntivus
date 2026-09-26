using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Admin;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Application.Security
{
    public sealed class AdminUserRoleService : IAdminUserRoleService
    {
        private static readonly TimeSpan AuditRetention = TimeSpan.FromDays(3650);

        private readonly IAdminUserRoleRepository _repository;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<AdminUserRoleService> _logger;

        public AdminUserRoleService(
            IAdminUserRoleRepository repository,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<AdminUserRoleService> logger)
        {
            _repository = repository;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<AdminUserRoleAssignmentView>>> ListAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _repository.ListAsync(cancellationToken);
                if (result.IsFailure)
                    return Result<IReadOnlyList<AdminUserRoleAssignmentView>>.Fail(result.Errors);

                var views = result.Value
                    .Select(static value => new AdminUserRoleAssignmentView(
                        value.Subject,
                        value.DisplayName,
                        value.Roles,
                        value.CreatedAt,
                        value.UpdatedAt))
                    .ToArray();

                return Result.Success<IReadOnlyList<AdminUserRoleAssignmentView>>(views);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to list admin users.");
                return Result.Failure<IReadOnlyList<AdminUserRoleAssignmentView>>(ex);
            }
        }

        public async Task<Result<AdminUserRoleAssignmentView>> GetAsync(
            string subject,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure<AdminUserRoleAssignmentView>(Error.Validation("subject is required."));

            try
            {
                var result = await _repository.GetBySubjectAsync(subject.Trim(), cancellationToken);
                if (result.IsFailure)
                    return Result<AdminUserRoleAssignmentView>.Fail(result.Errors);

                if (result.Value is null)
                    return Result.Failure<AdminUserRoleAssignmentView>(Error.NotFound($"Admin user '{subject}' was not found."));

                var value = result.Value;
                return Result.Success(new AdminUserRoleAssignmentView(
                    value.Subject,
                    value.DisplayName,
                    value.Roles,
                    value.CreatedAt,
                    value.UpdatedAt));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get admin user {Subject}.", subject);
                return Result.Failure<AdminUserRoleAssignmentView>(ex);
            }
        }

        public async Task<Result<AdminUserRoleAssignmentView>> ReplaceAsync(
            string subject,
            string? displayName,
            IReadOnlyList<string> roles,
            string? actorId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure<AdminUserRoleAssignmentView>(Error.Validation("subject is required."));

            var normalizedRoles = roles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedRoles.Length == 0)
                return Result.Failure<AdminUserRoleAssignmentView>(Error.Validation("At least one role is required."));

            var now = DateTimeOffset.UtcNow;

            try
            {
                _unitOfWork.Begin(true);

                var replace = await _repository.ReplaceAsync(
                    subject.Trim(),
                    displayName,
                    normalizedRoles,
                    now,
                    _unitOfWork,
                    cancellationToken);

                if (replace.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<AdminUserRoleAssignmentView>.Fail(replace.Errors);
                }

                var audit = AuditLogEntry.Create(
                    category: "admin-user",
                    action: "roles-replaced",
                    actorId: actorId,
                    customerId: null,
                    contractId: null,
                    deliveryId: null,
                    eventId: null,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        subject = subject.Trim(),
                        displayName,
                        roles = normalizedRoles,
                    }),
                    createdAt: now,
                    expiresAt: now.Add(AuditRetention));

                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<AdminUserRoleAssignmentView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();

                var get = await _repository.GetBySubjectAsync(subject.Trim(), cancellationToken);
                if (get.IsFailure)
                    return Result<AdminUserRoleAssignmentView>.Fail(get.Errors);

                if (get.Value is null)
                    return Result.Failure<AdminUserRoleAssignmentView>(Error.NotFound($"Admin user '{subject}' was not found."));

                var value = get.Value;
                return Result.Success(new AdminUserRoleAssignmentView(
                    value.Subject,
                    value.DisplayName,
                    value.Roles,
                    value.CreatedAt,
                    value.UpdatedAt));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to replace admin roles for {Subject}.", subject);
                return Result.Failure<AdminUserRoleAssignmentView>(ex);
            }
        }

        public async Task<Result> DeleteAsync(
            string subject,
            string? actorId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure(Error.Validation("subject is required."));

            var now = DateTimeOffset.UtcNow;
            try
            {
                _unitOfWork.Begin(true);
                var removed = await _repository.DeleteBySubjectAsync(subject.Trim(), _unitOfWork, cancellationToken);
                if (removed.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result.Failure(removed.Errors);
                }

                var audit = AuditLogEntry.Create(
                    category: "admin-user",
                    action: removed.Value ? "deleted" : "delete-noop",
                    actorId: actorId,
                    customerId: null,
                    contractId: null,
                    deliveryId: null,
                    eventId: null,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        subject = subject.Trim(),
                        removed = removed.Value,
                    }),
                    createdAt: now,
                    expiresAt: now.Add(AuditRetention));

                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result.Failure(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                return Result.Success();
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to delete admin user {Subject}.", subject);
                return Result.Failure(ex);
            }
        }
    }
}