using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Settings;
using KairoServer.Domain.Auditing;

namespace KairoServer.Application.Settings
{
    public sealed class InstanceSettingsService : IInstanceSettingsService
    {
        private static readonly TimeSpan AuditRetention = TimeSpan.FromDays(3650);

        private readonly ISettingsRepository _repository;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<InstanceSettingsService> _logger;

        public InstanceSettingsService(
            ISettingsRepository repository,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<InstanceSettingsService> logger)
        {
            _repository = repository;
            _auditLogs = auditLogs;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<InstanceSettingsView>> GetAsync(CancellationToken cancellationToken)
        {
            var result = await _repository.GetInstanceSettingsAsync(cancellationToken);
            if (result.IsFailure)
                return Result<InstanceSettingsView>.Fail(result.Errors);

            var state = result.Value ?? DefaultState();
            return Result.Success(ToView(state));
        }

        public async Task<Result<InstanceSettingsView>> UpdateAsync(
            UpdateInstanceSettingsRequest request,
            string? actorId,
            CancellationToken cancellationToken)
        {
            var validation = Validate(request);
            if (validation is not null)
                return Result.Failure<InstanceSettingsView>(validation);

            var now = DateTimeOffset.UtcNow;
            var state = new InstanceSettingsState(
                request.DeliveryDefaults.RequestTimeoutSeconds,
                request.DeliveryDefaults.MaxAttempts,
                request.DeliveryDefaults.BatchLimit,
                request.RetryDefaults.InitialDelaySeconds,
                request.RetryDefaults.MaxDelaySeconds,
                request.RetryDefaults.BackoffMultiplier,
                request.Security.RequireHttpsDestinations,
                request.Security.AllowPrivateNetworkDestinations,
                request.Security.RequireDestinationCredentialEncryption,
                request.Retention.EventRetentionDays,
                request.Retention.DeliveryAuditRetentionDays,
                request.Retention.OperationAuditRetentionDays,
                request.Limits.MaxEventPayloadBytes,
                request.Limits.MaxEventMetadataBytes,
                request.Limits.MaxContractsPerCustomer,
                request.Limits.MaxConcurrentReplayJobs,
                now,
                actorId);

            try
            {
                _unitOfWork.Begin(true);

                var write = await _repository.ReplaceInstanceSettingsAsync(state, _unitOfWork, cancellationToken);
                if (write.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<InstanceSettingsView>.Fail(write.Errors);
                }

                var audit = AuditLogEntry.Create(
                    category: "settings",
                    action: "instance-updated",
                    actorId: actorId,
                    customerId: null,
                    contractId: null,
                    deliveryId: null,
                    eventId: null,
                    operationId: null,
                    details: JsonSerializer.Serialize(request),
                    createdAt: now,
                    expiresAt: now.Add(AuditRetention));

                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<InstanceSettingsView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                return Result.Success(ToView(state));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to update instance settings.");
                return Result.Failure<InstanceSettingsView>(ex);
            }
        }

        private static Error? Validate(UpdateInstanceSettingsRequest request)
        {
            if (request is null)
                return Error.Validation("request is required.");

            if (request.DeliveryDefaults.RequestTimeoutSeconds is < 1 or > 300)
                return Error.Validation("deliveryDefaults.requestTimeoutSeconds must be between 1 and 300.");

            if (request.DeliveryDefaults.MaxAttempts is < 1 or > 20)
                return Error.Validation("deliveryDefaults.maxAttempts must be between 1 and 20.");

            if (request.DeliveryDefaults.BatchLimit is < 1 or > 1000)
                return Error.Validation("deliveryDefaults.batchLimit must be between 1 and 1000.");

            if (request.RetryDefaults.InitialDelaySeconds is < 1 or > 3600)
                return Error.Validation("retryDefaults.initialDelaySeconds must be between 1 and 3600.");

            if (request.RetryDefaults.MaxDelaySeconds is < 1 or > 86400)
                return Error.Validation("retryDefaults.maxDelaySeconds must be between 1 and 86400.");

            if (request.RetryDefaults.InitialDelaySeconds > request.RetryDefaults.MaxDelaySeconds)
                return Error.Validation("retryDefaults.initialDelaySeconds must be less than or equal to maxDelaySeconds.");

            if (request.RetryDefaults.BackoffMultiplier is < 1.0 or > 10.0)
                return Error.Validation("retryDefaults.backoffMultiplier must be between 1.0 and 10.0.");

            if (request.Retention.EventRetentionDays is < 1 or > 3650)
                return Error.Validation("retention.eventRetentionDays must be between 1 and 3650.");

            if (request.Retention.DeliveryAuditRetentionDays is < 1 or > 3650)
                return Error.Validation("retention.deliveryAuditRetentionDays must be between 1 and 3650.");

            if (request.Retention.OperationAuditRetentionDays is < 1 or > 3650)
                return Error.Validation("retention.operationAuditRetentionDays must be between 1 and 3650.");

            if (request.Limits.MaxEventPayloadBytes is < 1024 or > 10485760)
                return Error.Validation("limits.maxEventPayloadBytes must be between 1024 and 10485760.");

            if (request.Limits.MaxEventMetadataBytes is < 256 or > 1048576)
                return Error.Validation("limits.maxEventMetadataBytes must be between 256 and 1048576.");

            if (request.Limits.MaxEventMetadataBytes > request.Limits.MaxEventPayloadBytes)
                return Error.Validation("limits.maxEventMetadataBytes must be less than or equal to maxEventPayloadBytes.");

            if (request.Limits.MaxContractsPerCustomer is < 1 or > 10000)
                return Error.Validation("limits.maxContractsPerCustomer must be between 1 and 10000.");

            if (request.Limits.MaxConcurrentReplayJobs is < 1 or > 100)
                return Error.Validation("limits.maxConcurrentReplayJobs must be between 1 and 100.");

            return null;
        }

        private static InstanceSettingsState DefaultState()
        {
            return new InstanceSettingsState(
                RequestTimeoutSeconds: 30,
                MaxAttempts: 6,
                BatchLimit: 500,
                InitialDelaySeconds: 30,
                MaxDelaySeconds: 3600,
                BackoffMultiplier: 2.0,
                RequireHttpsDestinations: true,
                AllowPrivateNetworkDestinations: false,
                RequireDestinationCredentialEncryption: true,
                EventRetentionDays: 90,
                DeliveryAuditRetentionDays: 365,
                OperationAuditRetentionDays: 365,
                MaxEventPayloadBytes: 262144,
                MaxEventMetadataBytes: 65536,
                MaxContractsPerCustomer: 500,
                MaxConcurrentReplayJobs: 5,
                UpdatedAt: DateTimeOffset.UtcNow,
                UpdatedBy: null);
        }

        private static InstanceSettingsView ToView(InstanceSettingsState state)
        {
            return new InstanceSettingsView(
                new InstanceDeliveryDefaultsSettingsView(
                    state.RequestTimeoutSeconds,
                    state.MaxAttempts,
                    state.BatchLimit),
                new InstanceRetryDefaultsSettingsView(
                    state.InitialDelaySeconds,
                    state.MaxDelaySeconds,
                    state.BackoffMultiplier),
                new InstanceSecuritySettingsView(
                    state.RequireHttpsDestinations,
                    state.AllowPrivateNetworkDestinations,
                    state.RequireDestinationCredentialEncryption),
                new InstanceRetentionSettingsView(
                    state.EventRetentionDays,
                    state.DeliveryAuditRetentionDays,
                    state.OperationAuditRetentionDays),
                new InstanceLimitsSettingsView(
                    state.MaxEventPayloadBytes,
                    state.MaxEventMetadataBytes,
                    state.MaxContractsPerCustomer,
                    state.MaxConcurrentReplayJobs),
                state.UpdatedAt,
                state.UpdatedBy);
        }
    }
}