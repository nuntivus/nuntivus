using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Contracts;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public class PostgresContractRepository : IContractRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresContractRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result> InsertAsync(
            Contract contract,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string insertContract = """
                    INSERT INTO contracts
                    (
                        id,
                        customer_id,
                        enabled,
                        destination_type,
                        destination_config,
                        transformation_config,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @Id,
                        @CustomerId,
                        @Enabled,
                        @DestinationType,
                        @DestinationConfig::jsonb,
                        CAST(@TransformationConfig AS jsonb),
                        @CreatedAt,
                        @UpdatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                await connection.ExecuteAsync(new CommandDefinition(
                    insertContract,
                    new
                    {
                        contract.Id,
                        contract.CustomerId,
                        contract.Enabled,
                        DestinationType = contract.Destination.Type.ToString(),
                        DestinationConfig = contract.Destination.Configuration,
                        TransformationConfig = SerializeTransformation(contract.Transformation),
                        contract.CreatedAt,
                        contract.UpdatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await InsertEventTypesAsync(connection, transaction, contract, cancellationToken);
                await InsertSchemaTargetsAsync(connection, transaction, contract, cancellationToken);
                await InsertFilterRulesAsync(connection, transaction, contract, cancellationToken);

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
            {
                return Result.Failure(Error.NotFound(
                    $"Customer '{contract.CustomerId}' was not found."));
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result> UpdateAsync(
            Contract contract,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!uow.IsTransactionOpen)
                    throw new InvalidOperationException("Transaction is not open.");

                const string updateContract = """
                    UPDATE contracts
                    SET enabled = @Enabled,
                        destination_type = @DestinationType,
                        destination_config = @DestinationConfig::jsonb,
                        transformation_config = CAST(@TransformationConfig AS jsonb),
                        updated_at = @UpdatedAt
                    WHERE id = @Id;
                    """;

                const string deleteEventTypes = """
                    DELETE FROM contract_event_types WHERE contract_id = @Id;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    updateContract,
                    new
                    {
                        contract.Id,
                        contract.Enabled,
                        DestinationType = contract.Destination.Type.ToString(),
                        DestinationConfig = contract.Destination.Configuration,
                        TransformationConfig = SerializeTransformation(contract.Transformation),
                        contract.UpdatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                if (rows == 0)
                    return Result.Failure(Error.NotFound($"Contract '{contract.Id}' was not found."));

                await connection.ExecuteAsync(new CommandDefinition(
                    deleteEventTypes,
                    new { contract.Id },
                    transaction,
                    cancellationToken: cancellationToken));

                await InsertEventTypesAsync(connection, transaction, contract, cancellationToken);
                await DeleteSchemaTargetsAsync(connection, transaction, contract.Id, cancellationToken);
                await InsertSchemaTargetsAsync(connection, transaction, contract, cancellationToken);
                await DeleteFilterRulesAsync(connection, transaction, contract.Id, cancellationToken);
                await InsertFilterRulesAsync(connection, transaction, contract, cancellationToken);

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public async Task<Result<Contract?>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, customer_id, enabled, destination_type, destination_config, transformation_config::text AS transformation_config,
                           created_at, updated_at
                    FROM contracts
                    WHERE id = @Id
                    LIMIT 1;
                    """;

                const string eventTypesSql = """
                    SELECT event_type FROM contract_event_types WHERE contract_id = @Id;
                    """;

                const string schemaTargetsSql = """
                    SELECT event_type, schema_version
                    FROM contract_schema_targets
                    WHERE contract_id = @Id;
                    """;

                const string filterRulesSql = """
                    SELECT contract_id, scope, rule_order, selector, operator, value, values_json::text AS values_json
                    FROM contract_filter_rules
                    WHERE contract_id = @Id
                    ORDER BY scope ASC, rule_order ASC;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<ContractRow>(
                    new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Contract?>(null);

                var eventTypes = await connection.QueryAsync<string>(
                    new CommandDefinition(eventTypesSql, new { Id = id }, cancellationToken: cancellationToken));

                var schemaTargets = await connection.QueryAsync<SchemaTargetRow>(
                    new CommandDefinition(schemaTargetsSql, new { Id = id }, cancellationToken: cancellationToken));

                var filterRules = await connection.QueryAsync<FilterRuleRow>(
                    new CommandDefinition(filterRulesSql, new { Id = id }, cancellationToken: cancellationToken));

                return Result.Success<Contract?>(
                    row.ToDomain(
                        eventTypes,
                        schemaTargets.Select(s => s.ToDomain()),
                        BuildFilter(filterRules)));
            }
            catch (Exception ex)
            {
                return Result.Failure<Contract?>(ex);
            }
        }

        public async Task<Result<PagedResult<Contract>>> ListByCustomerAsync(
            string customerId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                const string sql = """
                    SELECT id, customer_id, enabled, destination_type, destination_config, transformation_config::text AS transformation_config,
                           created_at, updated_at,
                           COUNT(*) OVER() AS total_count
                    FROM contracts
                    WHERE customer_id = @CustomerId
                    ORDER BY created_at DESC, id ASC
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                const string eventTypesSql = """
                    SELECT contract_id, event_type
                    FROM contract_event_types
                    WHERE contract_id = ANY(@Ids);
                    """;

                const string schemaTargetsSql = """
                    SELECT contract_id, event_type, schema_version
                    FROM contract_schema_targets
                    WHERE contract_id = ANY(@Ids);
                    """;

                const string filterRulesSql = """
                    SELECT contract_id, scope, rule_order, selector, operator, value, values_json::text AS values_json
                    FROM contract_filter_rules
                    WHERE contract_id = ANY(@Ids)
                    ORDER BY scope ASC, rule_order ASC;
                    """;

                var offset = (page - 1) * pageSize;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<ContractRowWithCount>(new CommandDefinition(
                    sql,
                    new { CustomerId = customerId, PageSize = pageSize, Offset = offset },
                    cancellationToken: cancellationToken));

                var rowList = new List<ContractRowWithCount>(rows);
                long totalCount = 0;
                var ids = new List<Guid>(rowList.Count);
                foreach (var r in rowList)
                {
                    totalCount = r.total_count;
                    ids.Add(r.id);
                }

                var eventTypesByContract = new Dictionary<Guid, List<string>>();
                var schemaTargetsByContract = new Dictionary<Guid, List<ContractSchemaTarget>>();
                var filtersByContract = new Dictionary<Guid, ContractFilter?>();
                if (ids.Count > 0)
                {
                    var etRows = await connection.QueryAsync<EventTypeRow>(new CommandDefinition(
                        eventTypesSql,
                        new { Ids = ids.ToArray() },
                        cancellationToken: cancellationToken));

                    foreach (var et in etRows)
                    {
                        if (!eventTypesByContract.TryGetValue(et.contract_id, out var list))
                        {
                            list = new List<string>();
                            eventTypesByContract[et.contract_id] = list;
                        }
                        list.Add(et.event_type);
                    }

                    var stRows = await connection.QueryAsync<SchemaTargetRow>(new CommandDefinition(
                        schemaTargetsSql,
                        new { Ids = ids.ToArray() },
                        cancellationToken: cancellationToken));

                    foreach (var st in stRows)
                    {
                        if (!schemaTargetsByContract.TryGetValue(st.contract_id, out var list))
                        {
                            list = new List<ContractSchemaTarget>();
                            schemaTargetsByContract[st.contract_id] = list;
                        }

                        list.Add(st.ToDomain());
                    }

                    var filterRows = await connection.QueryAsync<FilterRuleRow>(new CommandDefinition(
                        filterRulesSql,
                        new { Ids = ids.ToArray() },
                        cancellationToken: cancellationToken));

                    foreach (var group in filterRows.GroupBy(r => r.contract_id))
                    {
                        filtersByContract[group.Key] = BuildFilter(group);
                    }
                }

                var materialized = new List<Contract>(rowList.Count);
                foreach (var r in rowList)
                {
                    eventTypesByContract.TryGetValue(r.id, out var etList);
                    schemaTargetsByContract.TryGetValue(r.id, out var stList);
                    filtersByContract.TryGetValue(r.id, out var filter);
                    materialized.Add(r.ToDomain(
                        etList ?? new List<string>(),
                        stList ?? new List<ContractSchemaTarget>(),
                        filter));
                }

                return Result.Success(
                    PagedResult<Contract>.Create(materialized, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Contract>>(ex);
            }
        }

        public async Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(
            string eventType,
            CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(eventType))
                    return Result.Success<IReadOnlyList<ContractMatch>>(Array.Empty<ContractMatch>());

                const string sql = """
                    SELECT c.id                 AS ContractId,
                           c.customer_id        AS CustomerId,
                           c.destination_type   AS DestinationType,
                           c.destination_config AS DestinationConfig,
                           c.transformation_config::text AS TransformationConfig
                    FROM contracts c
                    INNER JOIN contract_event_types cet ON cet.contract_id = c.id
                    INNER JOIN customers cu             ON cu.id = c.customer_id
                    WHERE c.enabled = TRUE
                      AND cu.enabled = TRUE
                      AND cet.event_type = @EventType;
                    """;

                const string filterRulesSql = """
                    SELECT contract_id, scope, rule_order, selector, operator, value, values_json::text AS values_json
                    FROM contract_filter_rules
                    WHERE contract_id = ANY(@Ids)
                    ORDER BY scope ASC, rule_order ASC;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<(Guid ContractId, string CustomerId, string DestinationType, string DestinationConfig, string? TransformationConfig)>(
                    new CommandDefinition(
                        sql,
                        new { EventType = eventType },
                        cancellationToken: cancellationToken));

                var rowList = rows.ToList();
                var ids = rowList.Select(r => r.ContractId).Distinct().ToArray();
                var filtersByContract = new Dictionary<Guid, ContractFilter?>();

                if (ids.Length > 0)
                {
                    var filterRows = await connection.QueryAsync<FilterRuleRow>(new CommandDefinition(
                        filterRulesSql,
                        new { Ids = ids },
                        cancellationToken: cancellationToken));

                    foreach (var group in filterRows.GroupBy(r => r.contract_id))
                    {
                        filtersByContract[group.Key] = BuildFilter(group);
                    }
                }

                var matches = rowList
                    .Select(r => new ContractMatch(
                        r.ContractId,
                        r.CustomerId,
                        new Destination(
                            Enum.Parse<DestinationType>(r.DestinationType),
                            r.DestinationConfig),
                        filtersByContract.TryGetValue(r.ContractId, out var filter) ? filter : null,
                        DeserializeTransformation(r.TransformationConfig)))
                    .ToList();

                return Result.Success<IReadOnlyList<ContractMatch>>(matches);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<ContractMatch>>(ex);
            }
        }

        private static async Task InsertEventTypesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Contract contract,
            CancellationToken cancellationToken)
        {
            if (contract.EventTypes.Count == 0) return;

            const string sql = """
                INSERT INTO contract_event_types (contract_id, event_type)
                VALUES (@ContractId, @EventType);
                """;

            foreach (var et in contract.EventTypes)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new { ContractId = contract.Id, EventType = et },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        private static async Task InsertFilterRulesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Contract contract,
            CancellationToken cancellationToken)
        {
            if (contract.Filter is null || !contract.Filter.HasRules)
                return;

            const string sql = """
                INSERT INTO contract_filter_rules
                (contract_id, scope, rule_order, selector, operator, value, values_json)
                VALUES (@ContractId, @Scope, @RuleOrder, @Selector, @Operator, @Value, CAST(@ValuesJson AS jsonb));
                """;

            var metadataIndex = 0;
            foreach (var rule in contract.Filter.MetadataRules)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        ContractId = contract.Id,
                        Scope = "metadata",
                        RuleOrder = metadataIndex++,
                        rule.Selector,
                        Operator = rule.Operator.ToString(),
                        rule.Value,
                        ValuesJson = JsonSerializer.Serialize(rule.Values)
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            var payloadIndex = 0;
            foreach (var rule in contract.Filter.PayloadRules)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        ContractId = contract.Id,
                        Scope = "payload",
                        RuleOrder = payloadIndex++,
                        rule.Selector,
                        Operator = rule.Operator.ToString(),
                        rule.Value,
                        ValuesJson = JsonSerializer.Serialize(rule.Values)
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        private static async Task InsertSchemaTargetsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Contract contract,
            CancellationToken cancellationToken)
        {
            if (contract.SchemaTargets.Count == 0) return;

            const string sql = """
                INSERT INTO contract_schema_targets (contract_id, event_type, schema_version)
                VALUES (@ContractId, @EventType, @SchemaVersion);
                """;

            foreach (var target in contract.SchemaTargets)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        ContractId = contract.Id,
                        target.EventType,
                        SchemaVersion = target.Version
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        private static async Task DeleteSchemaTargetsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            const string sql = """
                DELETE FROM contract_schema_targets
                WHERE contract_id = @ContractId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { ContractId = contractId },
                transaction,
                cancellationToken: cancellationToken));
        }

        private static async Task DeleteFilterRulesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Guid contractId,
            CancellationToken cancellationToken)
        {
            const string sql = """
                DELETE FROM contract_filter_rules
                WHERE contract_id = @ContractId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { ContractId = contractId },
                transaction,
                cancellationToken: cancellationToken));
        }

        private static ContractFilter? BuildFilter(IEnumerable<FilterRuleRow> rows)
        {
            var metadata = new List<ContractFilterRule>();
            var payload = new List<ContractFilterRule>();

            foreach (var row in rows)
            {
                var rule = row.ToDomain();
                if (row.scope == "metadata")
                {
                    metadata.Add(rule);
                }
                else if (row.scope == "payload")
                {
                    payload.Add(rule);
                }
            }

            if (metadata.Count == 0 && payload.Count == 0)
                return null;

            return new ContractFilter(metadata, payload);
        }

        private static string? SerializeTransformation(ContractPayloadTransformation? transformation)
        {
            if (transformation is null)
                return null;

            return JsonSerializer.Serialize(transformation);
        }

        private static ContractPayloadTransformation? DeserializeTransformation(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<ContractPayloadTransformation>(json);
        }

        private NpgsqlConnection CreateConnection() =>
            (NpgsqlConnection)_connectionFactory.CreateConnection();

        private sealed class ContractRow
        {
            public Guid id { get; set; }
            public string customer_id { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public string destination_type { get; set; } = string.Empty;
            public string destination_config { get; set; } = string.Empty;
            public string? transformation_config { get; set; }
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }

            public Contract ToDomain(
                IEnumerable<string> eventTypes,
                IEnumerable<ContractSchemaTarget> schemaTargets,
                ContractFilter? filter)
            {
                var type = Enum.Parse<DestinationType>(destination_type, ignoreCase: true);
                var destination = new Destination(type, destination_config);
                return Contract.Rehydrate(
                    id,
                    customer_id,
                    enabled,
                    eventTypes,
                    schemaTargets,
                    filter,
                    destination,
                    created_at,
                    updated_at,
                    DeserializeTransformation(transformation_config));
            }
        }

        private sealed class ContractRowWithCount
        {
            public Guid id { get; set; }
            public string customer_id { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public string destination_type { get; set; } = string.Empty;
            public string destination_config { get; set; } = string.Empty;
            public string? transformation_config { get; set; }
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }
            public long total_count { get; set; }

            public Contract ToDomain(
                IEnumerable<string> eventTypes,
                IEnumerable<ContractSchemaTarget> schemaTargets,
                ContractFilter? filter)
            {
                var type = Enum.Parse<DestinationType>(destination_type, ignoreCase: true);
                var destination = new Destination(type, destination_config);
                return Contract.Rehydrate(
                    id,
                    customer_id,
                    enabled,
                    eventTypes,
                    schemaTargets,
                    filter,
                    destination,
                    created_at,
                    updated_at,
                    DeserializeTransformation(transformation_config));
            }
        }

        private sealed class EventTypeRow
        {
            public Guid contract_id { get; set; }
            public string event_type { get; set; } = string.Empty;
        }

        private sealed class SchemaTargetRow
        {
            public Guid contract_id { get; set; }
            public string event_type { get; set; } = string.Empty;
            public int schema_version { get; set; }

            public ContractSchemaTarget ToDomain() =>
                new(event_type, schema_version);
        }

        private sealed class FilterRuleRow
        {
            public Guid contract_id { get; set; }
            public string scope { get; set; } = string.Empty;
            public int rule_order { get; set; }
            public string selector { get; set; } = string.Empty;
            public string @operator { get; set; } = string.Empty;
            public string? value { get; set; }
            public string values_json { get; set; } = "[]";

            public ContractFilterRule ToDomain()
            {
                var values = JsonSerializer.Deserialize<string[]>(values_json) ?? Array.Empty<string>();
                return new ContractFilterRule(
                    selector,
                    Enum.Parse<ContractFilterOperator>(@operator, ignoreCase: true),
                    value,
                    values);
            }
        }
    }
}
