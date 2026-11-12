using System.Text.Json;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Events;
using NuntivusServer.Domain.Auditing;
using DomainSchema = NuntivusServer.Domain.Events.EventSchemaVersion;
using DomainSchemaState = NuntivusServer.Domain.Events.EventSchemaState;

namespace NuntivusServer.Application.Events
{
    public sealed class EventSchemaEvolutionService : IEventSchemaEvolutionService
    {
        private readonly IEventDefinitionRepository _repository;
        private readonly IEventSchemaValidator _validator;
        private readonly ISchemaCompatibilityAnalyzer _analyzer;
        private readonly IAuditLogRepository _audit;
        private readonly IUnitOfWork _uow;
        private readonly IAppLogger<EventSchemaEvolutionService> _logger;

        public EventSchemaEvolutionService(IEventDefinitionRepository repository, IEventSchemaValidator validator,
            ISchemaCompatibilityAnalyzer analyzer, IAuditLogRepository audit, IUnitOfWork uow, IAppLogger<EventSchemaEvolutionService> logger)
        {
            _repository = repository;
            _validator = validator;
            _analyzer = analyzer;
            _audit = audit;
            _uow = uow;
            _logger = logger;
        }

        public async Task<Result<SchemaCompatibilityReport>> CompareAsync(string eventType, int version, int? againstVersion, CancellationToken cancellationToken)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType) || version <= 0 || againstVersion is <= 0)
                return Result.Failure<SchemaCompatibilityReport>(Error.Validation("A valid event type and positive schema versions are required."));
            var candidate = await _repository.GetSchemaVersionAsync(eventType, version, cancellationToken);
            if (candidate.IsFailure) return Result<SchemaCompatibilityReport>.Fail(candidate.Errors);
            if (candidate.Value is null) return Result.Failure<SchemaCompatibilityReport>(Error.NotFound("The proposed schema version was not found."));
            var baseline = againstVersion.HasValue
                ? await _repository.GetSchemaVersionAsync(eventType, againstVersion.Value, cancellationToken)
                : await _repository.GetActiveSchemaVersionAsync(eventType, cancellationToken);
            if (baseline.IsFailure) return Result<SchemaCompatibilityReport>.Fail(baseline.Errors);
            if (againstVersion.HasValue && baseline.Value is null)
                return Result.Failure<SchemaCompatibilityReport>(Error.NotFound("The comparison schema version was not found."));
            return BuildReport(candidate.Value, baseline.Value);
        }

        public async Task<Result<EventSchemaVersionView>> ActivateAsync(string eventType, int version, CancellationToken cancellationToken,
            string? actorId, bool canOverride, SchemaActivationOptions? options)
        {
            if (!EventDefinitionValidation.IsValidEventType(eventType) || version <= 0)
                return Result.Failure<EventSchemaVersionView>(Error.Validation("A valid event type and positive schema version are required."));
            if (string.IsNullOrWhiteSpace(actorId))
                return Result.Failure<EventSchemaVersionView>(Error.Unauthorized("An authenticated operator identity is required."));
            options ??= new SchemaActivationOptions();
            if (options.OverrideCompatibility && !canOverride)
                return Result.Failure<EventSchemaVersionView>(Error.Forbidden("Only administrators can override compatibility checks."));
            if (options.OverrideCompatibility && (string.IsNullOrWhiteSpace(options.Reason) || options.Reason.Length > 2000))
                return Result.Failure<EventSchemaVersionView>(Error.Validation("An override reason of at most 2000 characters is required."));

            try
            {
                _uow.Begin(true);
                (await _repository.LockDefinitionAsync(eventType, _uow, cancellationToken)).EnsureSuccess();
                var candidate = (await _repository.GetSchemaVersionAsync(eventType, version, cancellationToken)).EnsureValue();
                if (candidate is null)
                {
                    _uow.Rollback();
                    return Result.Failure<EventSchemaVersionView>(Error.NotFound("The proposed schema version was not found."));
                }
                if (candidate.State == DomainSchemaState.Deprecated)
                {
                    _uow.Rollback();
                    return Result.Failure<EventSchemaVersionView>(Error.Validation("Deprecated schema versions cannot be activated."));
                }
                var baseline = (await _repository.GetActiveSchemaVersionAsync(eventType, cancellationToken)).EnsureValue();
                var report = BuildReport(candidate, baseline);
                if (report.IsFailure)
                {
                    _uow.Rollback();
                    return Result<EventSchemaVersionView>.Fail(report.Errors);
                }
                if (report.Value.Status != SchemaCompatibilityStatus.Compatible && !options.OverrideCompatibility)
                {
                    _uow.Rollback();
                    return Result.Failure<EventSchemaVersionView>(Error.Conflict(
                        $"Schema compatibility is {report.Value.Status}. An explicit administrator override with a reason is required."));
                }
                if (baseline?.Version == version)
                {
                    _uow.Commit();
                    return Result.Success(candidate.ToView());
                }
                var now = DateTimeOffset.UtcNow;
                (await _repository.ActivateSchemaVersionAsync(eventType, version, now, _uow, cancellationToken)).EnsureSuccess();
                var response = candidate.ToView() with { State = EventSchemaState.Active, ActivatedAt = now, DeprecatedAt = null };
                var snapshot = JsonSerializer.Serialize(new
                {
                    request = new { eventType, version, options.OverrideCompatibility, reason = options.Reason?.Trim() },
                    compatibility = report.Value,
                    response
                });
                var entry = AuditLogEntry.Create("event-schema", options.OverrideCompatibility ? "activation-overridden" : "activated",
                    actorId, null, null, null, null, null, snapshot, now, now.AddDays(90));
                (await _audit.AddAsync(entry, _uow, cancellationToken)).EnsureSuccess();
                _uow.Commit();
                return Result.Success(response);
            }
            catch (ResultFailureException ex)
            {
                _uow.Rollback();
                return Result<EventSchemaVersionView>.Fail(ex.Errors);
            }
            catch (Exception ex)
            {
                _uow.Rollback();
                _logger.Error(ex, "Failed to activate schema version {Version} for {EventType}.", version, eventType);
                return Result.Failure<EventSchemaVersionView>(ex);
            }
        }

        private Result<SchemaCompatibilityReport> BuildReport(DomainSchema candidate, DomainSchema? baseline)
        {
            var valid = _validator.ValidateSchema(candidate.Schema);
            if (valid.IsFailure) return Result<SchemaCompatibilityReport>.Fail(valid.Errors);
            var mode = (SchemaCompatibilityMode)candidate.CompatibilityMode;
            if (baseline is null)
                return Result.Success(new SchemaCompatibilityReport(candidate.EventType, candidate.Version, null, mode, SchemaCompatibilityStatus.Compatible, []));
            valid = _validator.ValidateSchema(baseline.Schema);
            if (valid.IsFailure) return Result<SchemaCompatibilityReport>.Fail(valid.Errors);
            return Result.Success(_analyzer.Compare(candidate.EventType, candidate.Version, baseline.Version, baseline.Schema, candidate.Schema, mode));
        }
    }
}
