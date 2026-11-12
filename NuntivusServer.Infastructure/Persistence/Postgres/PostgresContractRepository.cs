using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using Npgsql;

namespace NuntivusServer.Infastructure.Persistence.Postgres
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
                        name,
                        enabled,
                        ordering_enabled,
                        ordering_key_selector,
                        destination_type,
                        destination_config,
                        auth_ref,
                        auth_version,
                        transformation_config,
                        transformation_identity,
                        rate_limit_per_second,
                        rate_limit_per_minute,
                        rate_limit_burst,
                        created_at,
                        updated_at
                    )
                    VALUES
                    (
                        @Id,
                        @CustomerId,
                        @Name,
                        @Enabled,
                        @OrderingEnabled,
                        @OrderingKeySelector,
                        @DestinationType,
                        @DestinationConfig::jsonb,
                        @AuthenticationReference,
                        @AuthenticationVersion,
                        CAST(@TransformationConfig AS jsonb),
                        @TransformationIdentity,
                        @RateLimitPerSecond,
                        @RateLimitPerMinute,
                        @RateLimitBurst,
                        @CreatedAt,
                        @UpdatedAt
                    );
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;
                var authSnapshot = ExtractAuthenticationSnapshot(contract.Destination);

                var secretValidation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    contract.Destination.Configuration, authSnapshot.AuthenticationReference, uow, cancellationToken);
                if (secretValidation.IsFailure)
                    return secretValidation;

                await connection.ExecuteAsync(new CommandDefinition(
                    insertContract,
                    new
                    {
                        contract.Id,
                        contract.CustomerId,
                        contract.Name,
                        contract.Enabled,
                        OrderingEnabled = contract.Ordering is not null,
                        OrderingKeySelector = contract.Ordering?.KeySelector,
                        DestinationType = contract.Destination.Type.ToString(),
                        DestinationConfig = contract.Destination.Configuration,
                        AuthenticationReference = authSnapshot.AuthenticationReference,
                        AuthenticationVersion = authSnapshot.AuthenticationVersion,
                        TransformationConfig = SerializeTransformation(contract.Transformation),
                        TransformationIdentity = contract.Transformation?.Identity,
                        RateLimitPerSecond = contract.RateLimit?.PerSecond,
                        RateLimitPerMinute = contract.RateLimit?.PerMinute,
                        RateLimitBurst = contract.RateLimit?.Burst,
                        contract.CreatedAt,
                        contract.UpdatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await InsertEventTypesAsync(connection, transaction, contract, cancellationToken);
                await InsertSourceIdsAsync(connection, transaction, contract, cancellationToken);
                var bindingSave = await SaveBindingsAsync(connection, transaction, contract, uow, cancellationToken);
                if (bindingSave.IsFailure) return bindingSave;
                await InsertSchemaTargetsAsync(connection, transaction, contract, cancellationToken);
                await InsertFilterRulesAsync(connection, transaction, contract, cancellationToken);

                return Result.Success();
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
            {
                return Result.Failure(Error.NotFound(
                    "Contract customer or Source was not found."));
            }
            catch (Exception ex)
            {
                return Result.Failure(Error.Unexpected(ex.Message));
            }
        }

        public async Task<Result<PagedResult<Contract>>> ListAsync(
            AdminContractListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                var where = new StringBuilder(" WHERE 1 = 1");
                var parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(query.CustomerId))
                {
                    where.Append(" AND c.customer_id = @CustomerId");
                    parameters.Add("CustomerId", query.CustomerId.Trim());
                }

                if (query.Enabled.HasValue)
                {
                    where.Append(" AND c.enabled = @Enabled");
                    parameters.Add("Enabled", query.Enabled.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.DestinationType))
                {
                    where.Append(" AND EXISTS (SELECT 1 FROM contract_destinations cd WHERE cd.contract_id = c.id AND cd.destination_type = @DestinationType)");
                    parameters.Add("DestinationType", query.DestinationType.Trim());
                }

                if (!string.IsNullOrWhiteSpace(query.EventType))
                {
                    where.Append(" AND EXISTS (");
                    where.Append("SELECT 1 FROM contract_event_types cetf ");
                    where.Append("WHERE cetf.contract_id = c.id AND cetf.event_type = @EventType");
                    where.Append(")");
                    parameters.Add("EventType", query.EventType.Trim());
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    where.Append(" AND (");
                    where.Append("CAST(c.id AS text) ILIKE @Search ");
                    where.Append("OR c.customer_id ILIKE @Search ");
                    where.Append("OR COALESCE(c.name, '') ILIKE @Search ");
                    where.Append("OR EXISTS (");
                    where.Append("SELECT 1 FROM contract_event_types cetf ");
                    where.Append("WHERE cetf.contract_id = c.id AND cetf.event_type ILIKE @Search");
                    where.Append(")");
                    where.Append(")");
                    parameters.Add("Search", $"%{query.Search.Trim()}%");
                }

                if (query.CreatedFrom.HasValue)
                {
                    where.Append(" AND c.created_at >= @CreatedFrom");
                    parameters.Add("CreatedFrom", query.CreatedFrom.Value);
                }

                if (query.CreatedTo.HasValue)
                {
                    where.Append(" AND c.created_at <= @CreatedTo");
                    parameters.Add("CreatedTo", query.CreatedTo.Value);
                }

                var sortColumn = ResolveContractSortColumn(query.SortBy);
                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                var countSql = "SELECT COUNT(*) FROM contracts c" + where;
                parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                parameters.Add("PageSize", query.PageSize);

                var pageSql = $"""
                    SELECT
                        c.id,
                        c.customer_id,
                        c.name,
                        c.enabled,
                        c.ordering_enabled,
                        c.ordering_key_selector,
                        c.destination_type,
                        c.destination_config,
                        c.transformation_config::text AS transformation_config,
                        c.rate_limit_per_second,
                        c.rate_limit_per_minute,
                        c.rate_limit_burst,
                        c.created_at,
                        c.updated_at
                    FROM contracts c
                    {where}
                    ORDER BY {sortColumn} {sortDirection}, c.id {sortDirection}
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

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    countSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rows = await connection.QueryAsync<ContractRow>(new CommandDefinition(
                    pageSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rowList = new List<ContractRow>(rows);
                var ids = new List<Guid>(rowList.Count);
                foreach (var r in rowList)
                {
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

                var sourceIdsByContract = await LoadSourceIdsAsync(connection, ids, cancellationToken);
                var bindingsByContract = await LoadBindingsAsync(connection, ids, cancellationToken);
                var materialized = new List<Contract>(rowList.Count);
                foreach (var r in rowList)
                {
                    eventTypesByContract.TryGetValue(r.id, out var etList);
                    schemaTargetsByContract.TryGetValue(r.id, out var stList);
                    filtersByContract.TryGetValue(r.id, out var filter);
                    materialized.Add(r.ToDomain(
                        etList ?? new List<string>(),
                        stList ?? new List<ContractSchemaTarget>(),
                        filter,
                        sourceIdsByContract.GetValueOrDefault(r.id) ?? [],
                        bindingsByContract.GetValueOrDefault(r.id) ?? []));
                }

                return Result.Success(
                    PagedResult<Contract>.Create(materialized, query.Page, query.PageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Contract>>(ex);
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
                    SET name = @Name,
                        enabled = @Enabled,
                        ordering_enabled = @OrderingEnabled,
                        ordering_key_selector = @OrderingKeySelector,
                        destination_type = @DestinationType,
                        destination_config = @DestinationConfig::jsonb,
                        auth_ref = @AuthenticationReference,
                        auth_version = @AuthenticationVersion,
                        transformation_config = CAST(@TransformationConfig AS jsonb),
                        transformation_identity = @TransformationIdentity,
                        rate_limit_per_second = @RateLimitPerSecond,
                        rate_limit_per_minute = @RateLimitPerMinute,
                        rate_limit_burst = @RateLimitBurst,
                        updated_at = @UpdatedAt
                    WHERE id = @Id;
                    """;

                const string deleteEventTypes = """
                    DELETE FROM contract_event_types WHERE contract_id = @Id;
                    """;

                var connection = uow.GetConnection()!;
                var transaction = uow.GetTransaction()!;
                var authSnapshot = ExtractAuthenticationSnapshot(contract.Destination);

                var secretValidation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    contract.Destination.Configuration, authSnapshot.AuthenticationReference, uow, cancellationToken);
                if (secretValidation.IsFailure)
                    return secretValidation;

                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    updateContract,
                    new
                    {
                        contract.Id,
                        contract.Name,
                        contract.Enabled,
                        OrderingEnabled = contract.Ordering is not null,
                        OrderingKeySelector = contract.Ordering?.KeySelector,
                        DestinationType = contract.Destination.Type.ToString(),
                        DestinationConfig = contract.Destination.Configuration,
                        AuthenticationReference = authSnapshot.AuthenticationReference,
                        AuthenticationVersion = authSnapshot.AuthenticationVersion,
                        TransformationConfig = SerializeTransformation(contract.Transformation),
                        TransformationIdentity = contract.Transformation?.Identity,
                        RateLimitPerSecond = contract.RateLimit?.PerSecond,
                        RateLimitPerMinute = contract.RateLimit?.PerMinute,
                        RateLimitBurst = contract.RateLimit?.Burst,
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
                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM contract_sources WHERE contract_id = @Id", new { contract.Id }, transaction,
                    cancellationToken: cancellationToken));
                await InsertSourceIdsAsync(connection, transaction, contract, cancellationToken);
                var bindingSave = await SaveBindingsAsync(connection, transaction, contract, uow, cancellationToken);
                if (bindingSave.IsFailure) return bindingSave;
                await DeleteSchemaTargetsAsync(connection, transaction, contract.Id, cancellationToken);
                await InsertSchemaTargetsAsync(connection, transaction, contract, cancellationToken);
                await DeleteFilterRulesAsync(connection, transaction, contract.Id, cancellationToken);
                await InsertFilterRulesAsync(connection, transaction, contract, cancellationToken);

                return Result.Success();
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                return Result.Failure(Error.NotFound("Contract Source was not found."));
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
                    SELECT id, customer_id, name, enabled, ordering_enabled, ordering_key_selector, destination_type, destination_config, transformation_config::text AS transformation_config,
                           rate_limit_per_second, rate_limit_per_minute, rate_limit_burst,
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

                var sourceIds = await connection.QueryAsync<Guid>(new CommandDefinition(
                    "SELECT source_id FROM contract_sources WHERE contract_id = @Id ORDER BY source_id",
                    new { Id = id }, cancellationToken: cancellationToken));
                var bindings = await LoadBindingsAsync(connection, [id], cancellationToken);

                return Result.Success<Contract?>(
                    row.ToDomain(
                        eventTypes,
                        schemaTargets.Select(s => s.ToDomain()),
                        BuildFilter(filterRules),
                        sourceIds,
                        bindings.GetValueOrDefault(id) ?? []));
            }
            catch (Exception ex)
            {
                return Result.Failure<Contract?>(ex);
            }
        }

        public async Task<Result<PagedResult<Contract>>> ListByCustomerAsync(
            ContractListQuery query,
            CancellationToken cancellationToken)
        {
            try
            {
                var where = new StringBuilder(" WHERE c.customer_id = @CustomerId");
                var parameters = new DynamicParameters();
                parameters.Add("CustomerId", query.CustomerId);

                if (query.Enabled.HasValue)
                {
                    where.Append(" AND c.enabled = @Enabled");
                    parameters.Add("Enabled", query.Enabled.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.EventType))
                {
                    where.Append(" AND EXISTS (");
                    where.Append("SELECT 1 FROM contract_event_types cetf ");
                    where.Append("WHERE cetf.contract_id = c.id AND cetf.event_type = @EventType)");
                    parameters.Add("EventType", query.EventType.Trim());
                }

                if (query.CreatedFrom.HasValue)
                {
                    where.Append(" AND c.created_at >= @CreatedFrom");
                    parameters.Add("CreatedFrom", query.CreatedFrom.Value);
                }

                if (query.CreatedTo.HasValue)
                {
                    where.Append(" AND c.created_at <= @CreatedTo");
                    parameters.Add("CreatedTo", query.CreatedTo.Value);
                }

                var sortColumn = ResolveContractSortColumn(query.SortBy);
                var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? "ASC"
                    : "DESC";

                var countSql = "SELECT COUNT(*) FROM contracts c" + where;
                parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                parameters.Add("PageSize", query.PageSize);

                var pageSql = $"""
                    SELECT
                        c.id,
                        c.customer_id,
                        c.name,
                        c.enabled,
                        c.ordering_enabled,
                        c.ordering_key_selector,
                        c.destination_type,
                        c.destination_config,
                        c.transformation_config::text AS transformation_config,
                        c.rate_limit_per_second,
                        c.rate_limit_per_minute,
                        c.rate_limit_burst,
                        c.created_at,
                        c.updated_at
                    FROM contracts c
                    {where}
                    ORDER BY {sortColumn} {sortDirection}, c.id {sortDirection}
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

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    countSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rows = await connection.QueryAsync<ContractRow>(new CommandDefinition(
                    pageSql,
                    parameters,
                    cancellationToken: cancellationToken));

                var rowList = new List<ContractRow>(rows);
                var ids = new List<Guid>(rowList.Count);
                foreach (var r in rowList)
                {
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

                var sourceIdsByContract = await LoadSourceIdsAsync(connection, ids, cancellationToken);
                var bindingsByContract = await LoadBindingsAsync(connection, ids, cancellationToken);
                var materialized = new List<Contract>(rowList.Count);
                foreach (var r in rowList)
                {
                    eventTypesByContract.TryGetValue(r.id, out var etList);
                    schemaTargetsByContract.TryGetValue(r.id, out var stList);
                    filtersByContract.TryGetValue(r.id, out var filter);
                    materialized.Add(r.ToDomain(
                        etList ?? new List<string>(),
                        stList ?? new List<ContractSchemaTarget>(),
                        filter,
                        sourceIdsByContract.GetValueOrDefault(r.id) ?? [],
                        bindingsByContract.GetValueOrDefault(r.id) ?? []));
                }

                return Result.Success(
                    PagedResult<Contract>.Create(materialized, query.Page, query.PageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Contract>>(ex);
            }
        }

        private static string ResolveContractSortColumn(string? sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return "c.created_at";

            if (string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase))
                return "c.name";

            if (string.Equals(sortBy, "customerId", StringComparison.OrdinalIgnoreCase))
                return "c.customer_id";

            if (string.Equals(sortBy, "destinationType", StringComparison.OrdinalIgnoreCase))
                return "c.destination_type";

            if (string.Equals(sortBy, "updatedAt", StringComparison.OrdinalIgnoreCase))
                return "c.updated_at";

            return "c.created_at";
        }

        public async Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(
            string eventType,
            string sourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(eventType) || !Guid.TryParse(sourceId, out var parsedSourceId))
                    return Result.Success<IReadOnlyList<ContractMatch>>(Array.Empty<ContractMatch>());

                const string sql = """
                    SELECT c.id                 AS ContractId,
                           c.customer_id        AS CustomerId,
                           cd.id                AS DestinationBindingId,
                           c.ordering_enabled   AS OrderingEnabled,
                           c.ordering_key_selector AS OrderingKeySelector,
                           cd.destination_type  AS DestinationType,
                           cd.destination_config AS DestinationConfig,
                           cd.auth_ref          AS AuthenticationReference,
                           cd.auth_version      AS AuthenticationVersion,
                           c.transformation_config::text AS TransformationConfig,
                           cst.schema_version    AS SchemaVersion,
                           c.rate_limit_per_second AS RateLimitPerSecond,
                           c.rate_limit_per_minute AS RateLimitPerMinute,
                           c.rate_limit_burst      AS RateLimitBurst
                    FROM contracts c
                    INNER JOIN contract_destinations cd ON cd.contract_id = c.id AND cd.delivery_enabled = TRUE
                    INNER JOIN contract_event_types cet ON cet.contract_id = c.id
                    INNER JOIN contract_sources cs ON cs.contract_id = c.id AND cs.source_id = @SourceId
                    INNER JOIN customers cu             ON cu.id = c.customer_id
                    LEFT JOIN contract_schema_targets cst
                        ON cst.contract_id = c.id
                       AND cst.event_type = cet.event_type
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

                var rows = await connection.QueryAsync<(Guid ContractId, string CustomerId, Guid DestinationBindingId, bool OrderingEnabled, string? OrderingKeySelector, string DestinationType, string DestinationConfig, string? AuthenticationReference, int? AuthenticationVersion, string? TransformationConfig, int? SchemaVersion, int? RateLimitPerSecond, int? RateLimitPerMinute, int? RateLimitBurst)>(
                    new CommandDefinition(
                        sql,
                        new { EventType = eventType, SourceId = parsedSourceId },
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
                    .Select(r =>
                    {
                        var transformation = DeserializeTransformation(r.TransformationConfig);
                        return new ContractMatch(
                            r.ContractId,
                            r.CustomerId,
                            new Destination(
                                Enum.Parse<DestinationType>(r.DestinationType),
                                r.DestinationConfig),
                            filtersByContract.TryGetValue(r.ContractId, out var filter) ? filter : null,
                            transformation,
                            ContractRateLimit.Create(r.RateLimitPerSecond, r.RateLimitPerMinute, r.RateLimitBurst),
                            r.OrderingEnabled ? ContractOrdering.Create(r.OrderingKeySelector) : null,
                            r.AuthenticationReference,
                            r.AuthenticationVersion,
                            transformation?.Version,
                            r.SchemaVersion,
                            r.DestinationBindingId);
                    })
                    .ToList();

                return Result.Success<IReadOnlyList<ContractMatch>>(matches);
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<ContractMatch>>(ex);
            }
        }

        private static async Task<Result> SaveBindingsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Contract contract,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            foreach (var binding in contract.Destinations)
            {
                var auth = ExtractAuthenticationSnapshot(binding.Destination);
                var authRef = binding.AuthenticationReference ?? auth.AuthenticationReference;
                var authVersion = binding.AuthenticationVersion ?? auth.AuthenticationVersion;
                var validation = await ManagedSecretReferenceValidator.ValidateAndLockAsync(
                    binding.Destination.Configuration, authRef, uow, cancellationToken);
                if (validation.IsFailure)
                    return validation;

                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO contract_destinations
                        (id, contract_id, destination_id, destination_type, destination_config,
                         auth_ref, auth_version, delivery_enabled, created_at)
                    VALUES (@Id, @ContractId, @DestinationId, @Type, CAST(@Config AS jsonb),
                            @AuthRef, @AuthVersion, @Enabled, @CreatedAt)
                    ON CONFLICT (id) DO UPDATE SET destination_id = EXCLUDED.destination_id,
                        destination_type = EXCLUDED.destination_type,
                        destination_config = EXCLUDED.destination_config,
                        auth_ref = EXCLUDED.auth_ref, auth_version = EXCLUDED.auth_version
                    """, new
                    {
                        binding.Id, ContractId = contract.Id, binding.DestinationId,
                        Type = binding.Destination.Type.ToString(), Config = binding.Destination.Configuration,
                        AuthRef = authRef, AuthVersion = authVersion,
                        Enabled = binding.DeliveryEnabled, binding.CreatedAt
                    }, transaction, cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM contract_destinations WHERE contract_id = @ContractId AND NOT (id = ANY(@Ids))",
                new { ContractId = contract.Id, Ids = contract.Destinations.Select(b => b.Id).ToArray() },
                transaction, cancellationToken: cancellationToken));
            return Result.Success();
        }

        private static async Task<Dictionary<Guid, List<ContractDestinationBinding>>> LoadBindingsAsync(
            System.Data.IDbConnection connection, List<Guid> contractIds, CancellationToken cancellationToken)
        {
            if (contractIds.Count == 0) return [];
            var rows = await connection.QueryAsync<DestinationBindingRow>(new CommandDefinition(
                "SELECT id, contract_id, destination_id, destination_type, destination_config::text AS destination_config, auth_ref, auth_version, delivery_enabled, created_at FROM contract_destinations WHERE contract_id = ANY(@Ids) ORDER BY created_at, id",
                new { Ids = contractIds.ToArray() }, cancellationToken: cancellationToken));
            return rows.GroupBy(row => row.contract_id)
                .ToDictionary(group => group.Key, group => group.Select(row => row.ToDomain()).ToList());
        }

        private sealed class DestinationBindingRow
        {
            public Guid id { get; set; }
            public Guid contract_id { get; set; }
            public Guid? destination_id { get; set; }
            public string destination_type { get; set; } = "";
            public string destination_config { get; set; } = "{}";
            public string? auth_ref { get; set; }
            public int? auth_version { get; set; }
            public bool delivery_enabled { get; set; }
            public DateTimeOffset created_at { get; set; }
            public ContractDestinationBinding ToDomain() => new(id, destination_id,
                new Destination(Enum.Parse<DestinationType>(destination_type), destination_config),
                delivery_enabled, created_at, auth_ref, auth_version);
        }

        private static async Task InsertSourceIdsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Contract contract,
            CancellationToken cancellationToken)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO contract_sources (contract_id, source_id) SELECT @ContractId, unnest(@SourceIds)",
                new { ContractId = contract.Id, SourceIds = contract.SourceIds.ToArray() }, transaction,
                cancellationToken: cancellationToken));
        }

        private static async Task<Dictionary<Guid, List<Guid>>> LoadSourceIdsAsync(
            System.Data.IDbConnection connection, List<Guid> contractIds, CancellationToken cancellationToken)
        {
            var assignments = new Dictionary<Guid, List<Guid>>();
            if (contractIds.Count == 0) return assignments;

            var rows = await connection.QueryAsync<(Guid contract_id, Guid source_id)>(new CommandDefinition(
                "SELECT contract_id, source_id FROM contract_sources WHERE contract_id = ANY(@Ids) ORDER BY source_id",
                new { Ids = contractIds.ToArray() }, cancellationToken: cancellationToken));
            foreach (var (contractId, sourceId) in rows)
            {
                if (!assignments.TryGetValue(contractId, out var sourceIds))
                    assignments[contractId] = sourceIds = [];
                sourceIds.Add(sourceId);
            }
            return assignments;
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

        private static (string? AuthenticationReference, int? AuthenticationVersion) ExtractAuthenticationSnapshot(
            Destination destination)
        {
            try
            {
                using var doc = JsonDocument.Parse(destination.Configuration);
                if (!doc.RootElement.TryGetProperty("Authentication", out var authentication)
                    || authentication.ValueKind != JsonValueKind.Object
                    || !authentication.TryGetProperty("Credential", out var credential)
                    || credential.ValueKind != JsonValueKind.Object)
                {
                    return (null, null);
                }

                var kind = credential.TryGetProperty("Kind", out var kindNode)
                    ? kindNode.GetString()?.Trim()
                    : null;
                var value = credential.TryGetProperty("Value", out var valueNode)
                    ? valueNode.GetString()?.Trim()
                    : null;

                if (!string.Equals(kind, "ref", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(value))
                    return (null, null);

                int? version = null;
                if (credential.TryGetProperty("Version", out var versionNode)
                    && versionNode.ValueKind == JsonValueKind.Number
                    && versionNode.TryGetInt32(out var parsedVersion)
                    && parsedVersion > 0)
                {
                    version = parsedVersion;
                }

                return (value, version);
            }
            catch (JsonException)
            {
                return (null, null);
            }
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
            public string? name { get; set; }
            public bool enabled { get; set; }
            public bool ordering_enabled { get; set; }
            public string? ordering_key_selector { get; set; }
            public string destination_type { get; set; } = string.Empty;
            public string destination_config { get; set; } = string.Empty;
            public string? transformation_config { get; set; }
            public int? rate_limit_per_second { get; set; }
            public int? rate_limit_per_minute { get; set; }
            public int? rate_limit_burst { get; set; }
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }

            public Contract ToDomain(
                IEnumerable<string> eventTypes,
                IEnumerable<ContractSchemaTarget> schemaTargets,
                ContractFilter? filter,
                IEnumerable<Guid> sourceIds,
                IEnumerable<ContractDestinationBinding> bindings)
            {
                var type = Enum.Parse<DestinationType>(destination_type, ignoreCase: true);
                var destination = new Destination(type, destination_config);
                return Contract.Rehydrate(
                    id,
                    customer_id,
                    name,
                    enabled,
                    eventTypes,
                    schemaTargets,
                    filter,
                    destination,
                    created_at,
                    updated_at,
                    DeserializeTransformation(transformation_config),
                    ContractRateLimit.Create(rate_limit_per_second, rate_limit_per_minute, rate_limit_burst),
                    ordering_enabled ? ContractOrdering.Create(ordering_key_selector) : null,
                    sourceIds,
                    bindings);
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
