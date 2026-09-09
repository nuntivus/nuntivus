using System;
using System.Collections.Generic;
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
                        contract.CreatedAt,
                        contract.UpdatedAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await InsertEventTypesAsync(connection, transaction, contract, cancellationToken);

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
                    SELECT id, customer_id, enabled, destination_type, destination_config,
                           created_at, updated_at
                    FROM contracts
                    WHERE id = @Id
                    LIMIT 1;
                    """;

                const string eventTypesSql = """
                    SELECT event_type FROM contract_event_types WHERE contract_id = @Id;
                    """;

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var row = await connection.QuerySingleOrDefaultAsync<ContractRow>(
                    new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

                if (row is null)
                    return Result.Success<Contract?>(null);

                var eventTypes = await connection.QueryAsync<string>(
                    new CommandDefinition(eventTypesSql, new { Id = id }, cancellationToken: cancellationToken));

                return Result.Success<Contract?>(row.ToDomain(eventTypes));
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
                    SELECT id, customer_id, enabled, destination_type, destination_config,
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
                }

                var materialized = new List<Contract>(rowList.Count);
                foreach (var r in rowList)
                {
                    eventTypesByContract.TryGetValue(r.id, out var etList);
                    materialized.Add(r.ToDomain(etList ?? new List<string>()));
                }

                return Result.Success(
                    PagedResult<Contract>.Create(materialized, page, pageSize, totalCount));
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResult<Contract>>(ex);
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

        private NpgsqlConnection CreateConnection() =>
            (NpgsqlConnection)_connectionFactory.CreateConnection();

        private sealed class ContractRow
        {
            public Guid id { get; set; }
            public string customer_id { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public string destination_type { get; set; } = string.Empty;
            public string destination_config { get; set; } = string.Empty;
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }

            public Contract ToDomain(IEnumerable<string> eventTypes)
            {
                var type = Enum.Parse<DestinationType>(destination_type, ignoreCase: true);
                var destination = new Destination(type, destination_config);
                return Contract.Rehydrate(
                    id, customer_id, enabled, eventTypes, destination, created_at, updated_at);
            }
        }

        private sealed class ContractRowWithCount
        {
            public Guid id { get; set; }
            public string customer_id { get; set; } = string.Empty;
            public bool enabled { get; set; }
            public string destination_type { get; set; } = string.Empty;
            public string destination_config { get; set; } = string.Empty;
            public DateTimeOffset created_at { get; set; }
            public DateTimeOffset updated_at { get; set; }
            public long total_count { get; set; }

            public Contract ToDomain(IEnumerable<string> eventTypes)
            {
                var type = Enum.Parse<DestinationType>(destination_type, ignoreCase: true);
                var destination = new Destination(type, destination_config);
                return Contract.Rehydrate(
                    id, customer_id, enabled, eventTypes, destination, created_at, updated_at);
            }
        }

        private sealed class EventTypeRow
        {
            public Guid contract_id { get; set; }
            public string event_type { get; set; } = string.Empty;
        }
    }
}
