using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Application.Settings;
using KairoServer.Common;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
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
                    SELECT key, value::text AS value, updated_at, updated_by
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

                if (delivery is null || retry is null || security is null || retention is null || limits is null)
                    return Result.Success<InstanceSettingsState?>(null);

                var deliveryPayload = JsonSerializer.Deserialize<DeliveryDefaultsPayload>(delivery.Value);
                var retryPayload = JsonSerializer.Deserialize<RetryDefaultsPayload>(retry.Value);
                var securityPayload = JsonSerializer.Deserialize<SecurityDefaultsPayload>(security.Value);
                var retentionPayload = JsonSerializer.Deserialize<RetentionDefaultsPayload>(retention.Value);
                var limitsPayload = JsonSerializer.Deserialize<LimitsDefaultsPayload>(limits.Value);

                if (deliveryPayload is null || retryPayload is null || securityPayload is null || retentionPayload is null || limitsPayload is null)
                    return Result.Success<InstanceSettingsState?>(null);

                var newest = rows
                    .OrderByDescending(static row => row.UpdatedAt)
                    .First();

                return Result.Success<InstanceSettingsState?>(new InstanceSettingsState(
                    deliveryPayload.RequestTimeoutSeconds,
                    deliveryPayload.MaxAttempts,
                    deliveryPayload.BatchLimit,
                    retryPayload.InitialDelaySeconds,
                    retryPayload.MaxDelaySeconds,
                    retryPayload.BackoffMultiplier,
                    securityPayload.RequireHttpsDestinations,
                    securityPayload.AllowPrivateNetworkDestinations,
                    securityPayload.RequireDestinationCredentialEncryption,
                    retentionPayload.EventRetentionDays,
                    retentionPayload.DeliveryAuditRetentionDays,
                    retentionPayload.OperationAuditRetentionDays,
                    limitsPayload.MaxEventPayloadBytes,
                    limitsPayload.MaxEventMetadataBytes,
                    limitsPayload.MaxContractsPerCustomer,
                    limitsPayload.MaxConcurrentReplayJobs,
                    newest.UpdatedAt,
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

                var deliveryJson = JsonSerializer.Serialize(new DeliveryDefaultsPayload(
                    state.RequestTimeoutSeconds,
                    state.MaxAttempts,
                    state.BatchLimit));

                var retryJson = JsonSerializer.Serialize(new RetryDefaultsPayload(
                    state.InitialDelaySeconds,
                    state.MaxDelaySeconds,
                    state.BackoffMultiplier));

                var securityJson = JsonSerializer.Serialize(new SecurityDefaultsPayload(
                    state.RequireHttpsDestinations,
                    state.AllowPrivateNetworkDestinations,
                    state.RequireDestinationCredentialEncryption));

                var retentionJson = JsonSerializer.Serialize(new RetentionDefaultsPayload(
                    state.EventRetentionDays,
                    state.DeliveryAuditRetentionDays,
                    state.OperationAuditRetentionDays));

                var limitsJson = JsonSerializer.Serialize(new LimitsDefaultsPayload(
                    state.MaxEventPayloadBytes,
                    state.MaxEventMetadataBytes,
                    state.MaxContractsPerCustomer,
                    state.MaxConcurrentReplayJobs));

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
                    SELECT value::text AS value, updated_at
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

                var payload = JsonSerializer.Deserialize<UserPreferencesPayload>(row.Value);
                if (payload is null)
                    return Result.Success<UserPreferencesState?>(null);

                return Result.Success<UserPreferencesState?>(new UserPreferencesState(
                    payload.Theme,
                    payload.DefaultPageSize,
                    payload.DenseTables,
                    row.UpdatedAt));
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
                var payload = JsonSerializer.Serialize(new UserPreferencesPayload(
                    state.Theme,
                    state.DefaultPageSize,
                    state.DenseTables));

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

        private sealed record InstanceSettingRow(string Key, string Value, DateTimeOffset UpdatedAt, string? UpdatedBy);
        private sealed record UserSettingRow(string Value, DateTimeOffset UpdatedAt);
        private sealed record DeliveryDefaultsPayload(int RequestTimeoutSeconds, int MaxAttempts, int BatchLimit);
        private sealed record RetryDefaultsPayload(int InitialDelaySeconds, int MaxDelaySeconds, double BackoffMultiplier);
        private sealed record SecurityDefaultsPayload(bool RequireHttpsDestinations, bool AllowPrivateNetworkDestinations, bool RequireDestinationCredentialEncryption);
        private sealed record RetentionDefaultsPayload(int EventRetentionDays, int DeliveryAuditRetentionDays, int OperationAuditRetentionDays);
        private sealed record LimitsDefaultsPayload(int MaxEventPayloadBytes, int MaxEventMetadataBytes, int MaxContractsPerCustomer, int MaxConcurrentReplayJobs);
        private sealed record UserPreferencesPayload(string Theme, int DefaultPageSize, bool DenseTables);
    }
}