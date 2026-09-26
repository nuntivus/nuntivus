using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Settings;
using NuntivusServer.Common;
using Npgsql;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresSettingsRepository : ISettingsRepository
    {
        private const string DeliveryDefaultsKey = "instance.delivery.defaults";
        private const string RetryDefaultsKey = "instance.retry.defaults";
        private const string SecurityDefaultsKey = "instance.security.defaults";
        private const string RetentionDefaultsKey = "instance.retention.defaults";
        private const string LimitsDefaultsKey = "instance.limits.defaults";
        private const string UserPreferencesKey = "user.preferences";

        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresSettingsRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result<InstanceSettingsState?>> GetInstanceSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT key, value::text AS value, updated_at AS UpdatedAt, updated_by AS UpdatedBy
                    FROM instance_settings
                    WHERE key = ANY(@Keys);
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = (await connection.QueryAsync<InstanceSettingRow>(new CommandDefinition(
                    sql,
                    new { Keys = new[] { DeliveryDefaultsKey, RetryDefaultsKey, SecurityDefaultsKey, RetentionDefaultsKey, LimitsDefaultsKey } },
                    cancellationToken: cancellationToken))).ToArray();

                var delivery = rows.FirstOrDefault(static row => row.Key == DeliveryDefaultsKey);
                var retry = rows.FirstOrDefault(static row => row.Key == RetryDefaultsKey);
                var security = rows.FirstOrDefault(static row => row.Key == SecurityDefaultsKey);
                var retention = rows.FirstOrDefault(static row => row.Key == RetentionDefaultsKey);
                var limits = rows.FirstOrDefault(static row => row.Key == LimitsDefaultsKey);

                if (delivery is null && retry is null && security is null && retention is null && limits is null)
                    return Result.Success<InstanceSettingsState?>(null);

                var deliveryPayload = delivery is null ? null : TryDeserialize<DeliveryDefaultsPayload>(delivery.Value);
                var retryPayload = retry is null ? null : TryDeserialize<RetryDefaultsPayload>(retry.Value);
                var securityPayload = security is null ? null : TryDeserialize<SecurityDefaultsPayload>(security.Value);
                var retentionPayload = retention is null ? null : TryDeserialize<RetentionDefaultsPayload>(retention.Value);
                var limitsPayload = limits is null ? null : TryDeserialize<LimitsDefaultsPayload>(limits.Value);

                var defaults = GetDefaultState();
                var requestTimeoutSeconds = deliveryPayload?.RequestTimeoutSeconds ?? defaults.RequestTimeoutSeconds;
                var maxAttempts = deliveryPayload?.MaxAttempts ?? defaults.MaxAttempts;
                var batchLimit = deliveryPayload?.BatchLimit ?? defaults.BatchLimit;
                var initialDelaySeconds = retryPayload?.InitialDelaySeconds ?? defaults.InitialDelaySeconds;
                var maxDelaySeconds = retryPayload?.MaxDelaySeconds ?? defaults.MaxDelaySeconds;
                var backoffMultiplier = retryPayload?.BackoffMultiplier ?? defaults.BackoffMultiplier;
                var requireHttpsDestinations = securityPayload?.RequireHttpsDestinations ?? defaults.RequireHttpsDestinations;
                var allowPrivateNetworkDestinations = securityPayload?.AllowPrivateNetworkDestinations ?? defaults.AllowPrivateNetworkDestinations;
                var requireDestinationCredentialEncryption = securityPayload?.RequireDestinationCredentialEncryption ?? defaults.RequireDestinationCredentialEncryption;
                var eventRetentionDays = retentionPayload?.EventRetentionDays ?? defaults.EventRetentionDays;
                var deliveryAuditRetentionDays = retentionPayload?.DeliveryAuditRetentionDays ?? defaults.DeliveryAuditRetentionDays;
                var operationAuditRetentionDays = retentionPayload?.OperationAuditRetentionDays ?? defaults.OperationAuditRetentionDays;
                var maxEventPayloadBytes = limitsPayload?.MaxEventPayloadBytes ?? defaults.MaxEventPayloadBytes;
                var maxEventMetadataBytes = limitsPayload?.MaxEventMetadataBytes ?? defaults.MaxEventMetadataBytes;
                var maxContractsPerCustomer = limitsPayload?.MaxContractsPerCustomer ?? defaults.MaxContractsPerCustomer;
                var maxConcurrentReplayJobs = limitsPayload?.MaxConcurrentReplayJobs ?? defaults.MaxConcurrentReplayJobs;

                var newest = rows
                    .OrderByDescending(static row => row.UpdatedAt)
                    .First();

                return Result.Success<InstanceSettingsState?>(new InstanceSettingsState(
                    requestTimeoutSeconds,
                    maxAttempts,
                    batchLimit,
                    initialDelaySeconds,
                    maxDelaySeconds,
                    backoffMultiplier,
                    requireHttpsDestinations,
                    allowPrivateNetworkDestinations,
                    requireDestinationCredentialEncryption,
                    eventRetentionDays,
                    deliveryAuditRetentionDays,
                    operationAuditRetentionDays,
                    maxEventPayloadBytes,
                    maxEventMetadataBytes,
                    maxContractsPerCustomer,
                    maxConcurrentReplayJobs,
                    ToUtcDateTimeOffset(newest.UpdatedAt),
                    newest.UpdatedBy));
            }
            catch (Exception ex)
            {
                return Result.Failure<InstanceSettingsState?>(ex);
            }
        }

        public async Task<Result> ReplaceInstanceSettingsAsync(
            InstanceSettingsState state,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                const string sql = """
                    INSERT INTO instance_settings(key, value, updated_at, updated_by)
                    VALUES (@Key, CAST(@Value AS jsonb), @UpdatedAt, @UpdatedBy)
                    ON CONFLICT (key)
                    DO UPDATE SET
                        value = EXCLUDED.value,
                        updated_at = EXCLUDED.updated_at,
                        updated_by = EXCLUDED.updated_by;
                    """;

                var deliveryJson = JsonSerializer.Serialize(new DeliveryDefaultsPayload
                {
                    RequestTimeoutSeconds = state.RequestTimeoutSeconds,
                    MaxAttempts = state.MaxAttempts,
                    BatchLimit = state.BatchLimit,
                });

                var retryJson = JsonSerializer.Serialize(new RetryDefaultsPayload
                {
                    InitialDelaySeconds = state.InitialDelaySeconds,
                    MaxDelaySeconds = state.MaxDelaySeconds,
                    BackoffMultiplier = state.BackoffMultiplier,
                });

                var securityJson = JsonSerializer.Serialize(new SecurityDefaultsPayload
                {
                    RequireHttpsDestinations = state.RequireHttpsDestinations,
                    AllowPrivateNetworkDestinations = state.AllowPrivateNetworkDestinations,
                    RequireDestinationCredentialEncryption = state.RequireDestinationCredentialEncryption,
                });

                var retentionJson = JsonSerializer.Serialize(new RetentionDefaultsPayload
                {
                    EventRetentionDays = state.EventRetentionDays,
                    DeliveryAuditRetentionDays = state.DeliveryAuditRetentionDays,
                    OperationAuditRetentionDays = state.OperationAuditRetentionDays,
                });

                var limitsJson = JsonSerializer.Serialize(new LimitsDefaultsPayload
                {
                    MaxEventPayloadBytes = state.MaxEventPayloadBytes,
                    MaxEventMetadataBytes = state.MaxEventMetadataBytes,
                    MaxContractsPerCustomer = state.MaxContractsPerCustomer,
                    MaxConcurrentReplayJobs = state.MaxConcurrentReplayJobs,
                });

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Key = DeliveryDefaultsKey,
                        Value = deliveryJson,
                        UpdatedAt = state.UpdatedAt,
                        UpdatedBy = state.UpdatedBy,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Key = SecurityDefaultsKey,
                        Value = securityJson,
                        UpdatedAt = state.UpdatedAt,
                        UpdatedBy = state.UpdatedBy,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Key = RetentionDefaultsKey,
                        Value = retentionJson,
                        UpdatedAt = state.UpdatedAt,
                        UpdatedBy = state.UpdatedBy,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Key = LimitsDefaultsKey,
                        Value = limitsJson,
                        UpdatedAt = state.UpdatedAt,
                        UpdatedBy = state.UpdatedBy,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Key = RetryDefaultsKey,
                        Value = retryJson,
                        UpdatedAt = state.UpdatedAt,
                        UpdatedBy = state.UpdatedBy,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<UserPreferencesState?>> GetUserPreferencesAsync(
            string subject,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT value::text AS value, updated_at AS UpdatedAt
                    FROM user_settings
                    WHERE subject = @Subject AND key = @Key
                    LIMIT 1;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<UserSettingRow>(new CommandDefinition(
                    sql,
                    new
                    {
                        Subject = subject,
                        Key = UserPreferencesKey,
                    },
                    cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<UserPreferencesState?>(null);

                var payload = TryDeserialize<UserPreferencesPayload>(row.Value);
                if (payload is null)
                    return Result.Success<UserPreferencesState?>(null);

                return Result.Success<UserPreferencesState?>(new UserPreferencesState(
                    payload.Theme,
                    payload.DefaultPageSize,
                    payload.DenseTables,
                    ToUtcDateTimeOffset(row.UpdatedAt)));
            }
            catch (Exception ex)
            {
                return Result.Failure<UserPreferencesState?>(ex);
            }
        }

        public async Task<Result> UpsertUserPreferencesAsync(
            string subject,
            UserPreferencesState state,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string sql = """
                    INSERT INTO user_settings(subject, key, value, updated_at)
                    VALUES (@Subject, @Key, CAST(@Value AS jsonb), @UpdatedAt)
                    ON CONFLICT (subject, key)
                    DO UPDATE SET
                        value = EXCLUDED.value,
                        updated_at = EXCLUDED.updated_at;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;
                var payload = JsonSerializer.Serialize(new UserPreferencesPayload
                {
                    Theme = state.Theme,
                    DefaultPageSize = state.DefaultPageSize,
                    DenseTables = state.DenseTables,
                });

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Subject = subject,
                        Key = UserPreferencesKey,
                        Value = payload,
                        UpdatedAt = state.UpdatedAt,
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        private static InstanceSettingsState GetDefaultState()
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

        private static T? TryDeserialize<T>(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
            => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private sealed record InstanceSettingRow(string Key, string Value, DateTime UpdatedAt, string? UpdatedBy);
        private sealed record UserSettingRow(string Value, DateTime UpdatedAt);
        private sealed class DeliveryDefaultsPayload
        {
            public int? RequestTimeoutSeconds { get; init; }
            public int? MaxAttempts { get; init; }
            public int? BatchLimit { get; init; }
        }

        private sealed class RetryDefaultsPayload
        {
            public int? InitialDelaySeconds { get; init; }
            public int? MaxDelaySeconds { get; init; }
            public double? BackoffMultiplier { get; init; }
        }

        private sealed class SecurityDefaultsPayload
        {
            public bool? RequireHttpsDestinations { get; init; }
            public bool? AllowPrivateNetworkDestinations { get; init; }
            public bool? RequireDestinationCredentialEncryption { get; init; }
        }

        private sealed class RetentionDefaultsPayload
        {
            public int? EventRetentionDays { get; init; }
            public int? DeliveryAuditRetentionDays { get; init; }
            public int? OperationAuditRetentionDays { get; init; }
        }

        private sealed class LimitsDefaultsPayload
        {
            public int? MaxEventPayloadBytes { get; init; }
            public int? MaxEventMetadataBytes { get; init; }
            public int? MaxContractsPerCustomer { get; init; }
            public int? MaxConcurrentReplayJobs { get; init; }
        }

        private sealed class UserPreferencesPayload
        {
            public string Theme { get; init; } = string.Empty;
            public int DefaultPageSize { get; init; }
            public bool DenseTables { get; init; }
        }
    }
}