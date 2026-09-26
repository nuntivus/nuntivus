using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Application.Security
{
    public sealed class AdminLocalCredentialService : IAdminLocalCredentialService
    {
        private static readonly TimeSpan AuditRetention = TimeSpan.FromDays(3650);

        private readonly IAdminLocalCredentialRepository _repository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<AdminLocalCredentialService> _logger;

        public AdminLocalCredentialService(
            IAdminLocalCredentialRepository repository,
            IPasswordHasher passwordHasher,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<AdminLocalCredentialService> logger)
        {
            _repository = repository;
            _passwordHasher = passwordHasher;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result> SetPasswordAsync(
            string subject,
            string password,
            string? actorId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Result.Failure(Error.Validation("subject is required."));

            var trimmedPassword = password?.Trim() ?? string.Empty;
            if (trimmedPassword.Length < 12)
                return Result.Failure(Error.Validation("password must be at least 12 characters long."));

            var now = DateTimeOffset.UtcNow;

            try
            {
                _unitOfWork.Begin(true);

                var hashed = _passwordHasher.Hash(trimmedPassword);
                var upsert = await _repository.UpsertAsync(
                    subject.Trim(),
                    hashed,
                    now,
                    _unitOfWork,
                    cancellationToken);

                if (upsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result.Failure(upsert.Errors);
                }

                var audit = AuditLogEntry.Create(
                    category: "admin-user",
                    action: "password-updated",
                    actorId: actorId,
                    customerId: null,
                    contractId: null,
                    deliveryId: null,
                    eventId: null,
                    operationId: null,
                    details: JsonSerializer.Serialize(new
                    {
                        subject = subject.Trim(),
                        passwordUpdated = true,
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
                _logger.Error(ex, "Failed to set local password for admin user {Subject}.", subject);
                return Result.Failure(ex);
            }
        }
    }
}
