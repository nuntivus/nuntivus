using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using Dapper;
using Npgsql;
using NuntivusServer.Application.Exports;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDataExportReader(IDatabaseConnectionFactory connections) : IDataExportReader
    {
        public async Task<Result<IDataExportSession>> OpenAsync(string entity, DataExportRequest request, CancellationToken cancellationToken)
        {
            if (entity is not ("events" or "deliveries" or "contracts") || request.Format is not ("csv" or "json"))
                return Result.Failure<IDataExportSession>(Error.Validation("Select events, deliveries or contracts and csv or json format."));
            if (request.CreatedFrom > request.CreatedTo || request.OccurredFrom > request.OccurredTo || request.ReceivedFrom > request.ReceivedTo)
                return Result.Failure<IDataExportSession>(Error.Validation("Filter start must not exceed end."));
            if (new[] { request.CustomerId, request.Type, request.Source, request.EventType, request.Status, request.DestinationType, request.FailureCategory, request.Search }.Any(value => value?.Length > 500))
                return Result.Failure<IDataExportSession>(Error.Validation("Filter values must not exceed 500 characters."));
            var connection = (NpgsqlConnection)connections.CreateConnection();
            NpgsqlTransaction? transaction = null;
            try
            {
                await connection.OpenAsync(cancellationToken);
                transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY", transaction: transaction, cancellationToken: cancellationToken));
                var cutoff = DateTimeOffset.UtcNow;
                var args = new DynamicParameters(new { Cutoff = cutoff });
                var where = new StringBuilder();
                string select, from, order;
                void Filter(string column, string name, object? value, string operation = "=")
                {
                    if (value is null || value is string text && string.IsNullOrWhiteSpace(text)) return;
                    where.Append($" AND {column} {operation} @{name}");
                    args.Add(name, value is string s ? s.Trim() : value);
                }
                if (entity == "events")
                {
                    select = "e.id AS id, e.type AS type, e.source_id AS sourceId, e.occurred_at AS occurredAt, e.received_at AS receivedAt, e.input_schema_version AS inputSchemaVersion, e.schema_validation_status AS schemaValidationStatus";
                    from = "events e"; order = "e.received_at, e.id";
                    where.Append("e.received_at <= @Cutoff");
                    Filter("e.id", "Id", request.Id); Filter("e.type", "Type", request.Type); Filter("e.source_id::text", "Source", request.Source);
                    Filter("e.occurred_at", "OccurredFrom", request.OccurredFrom, ">="); Filter("e.occurred_at", "OccurredTo", request.OccurredTo, "<=");
                    Filter("e.received_at", "ReceivedFrom", request.ReceivedFrom, ">="); Filter("e.received_at", "ReceivedTo", request.ReceivedTo, "<=");
                }
                else
                {
                    var alias = entity == "deliveries" ? "d" : "c";
                    from = $"{entity} {alias}"; order = $"{alias}.created_at, {alias}.id";
                    where.Append($"{alias}.created_at <= @Cutoff");
                    Filter($"{alias}.id", "Id", request.Id); Filter($"{alias}.customer_id", "CustomerId", request.CustomerId);
                    Filter($"{alias}.created_at", "CreatedFrom", request.CreatedFrom, ">="); Filter($"{alias}.created_at", "CreatedTo", request.CreatedTo, "<=");
                    if (entity == "deliveries")
                    {
                        select = "d.id AS id, d.event_id AS eventId, d.contract_id AS contractId, d.customer_id AS customerId, d.event_type AS eventType, d.destination_type AS destinationType, d.status AS status, d.attempt_count AS attemptCount, d.created_at AS createdAt, d.updated_at AS updatedAt";
                        Filter("d.event_id", "EventId", request.EventId); Filter("d.contract_id", "ContractId", request.ContractId);
                        Filter("d.status", "Status", request.Status); Filter("d.destination_type", "DestinationType", request.DestinationType); Filter("d.event_type", "EventType", request.EventType);
                        if (request.FailuresOnly)
                            where.Append(" AND d.status IN ('Failed', 'Dead')");
                        if (!string.IsNullOrWhiteSpace(request.FailureCategory))
                        {
                            where.Append(" AND EXISTS(SELECT 1 FROM delivery_attempts a WHERE a.delivery_id = d.id AND NOT a.succeeded AND a.failure_category = @FailureCategory)");
                            args.Add("FailureCategory", request.FailureCategory.Trim());
                        }
                    }
                    else
                    {
                        select = "c.id AS id, c.customer_id AS customerId, c.enabled AS enabled, c.created_at AS createdAt, c.updated_at AS updatedAt";
                        Filter("c.enabled", "Enabled", request.Enabled);
                        if (!string.IsNullOrWhiteSpace(request.EventType)) { where.Append(" AND EXISTS(SELECT 1 FROM contract_event_types t WHERE t.contract_id = c.id AND t.event_type = @EventType)"); args.Add("EventType", request.EventType.Trim()); }
                        if (!string.IsNullOrWhiteSpace(request.DestinationType)) { where.Append(" AND EXISTS(SELECT 1 FROM contract_destinations b WHERE b.contract_id = c.id AND b.destination_type = @DestinationType)"); args.Add("DestinationType", request.DestinationType.Trim()); }
                        if (!string.IsNullOrWhiteSpace(request.Search))
                        {
                            where.Append(" AND (c.id::text ILIKE @Search OR c.customer_id ILIKE @Search OR COALESCE(c.name, '') ILIKE @Search OR EXISTS(SELECT 1 FROM contract_event_types t WHERE t.contract_id = c.id AND t.event_type ILIKE @Search))");
                            args.Add("Search", $"%{request.Search.Trim()}%");
                        }
                    }
                }
                var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT count(*) FROM (SELECT 1 FROM {from} WHERE {where} LIMIT 10001) bounded", args, transaction, commandTimeout: 30, cancellationToken: cancellationToken));
                if (count > 10000)
                {
                    await transaction.DisposeAsync(); await connection.DisposeAsync();
                    return Result.Failure<IDataExportSession>(Error.Validation("Export exceeds 10000 rows. Narrow the filters and retry."));
                }
                var reader = await connection.ExecuteReaderAsync(new CommandDefinition($"SELECT {select} FROM {from} WHERE {where} ORDER BY {order}", args, transaction, commandTimeout: 60, cancellationToken: cancellationToken));
                return Result.Success<IDataExportSession>(new Session(connection, transaction, reader, count, cutoff));
            }
            catch (Exception ex)
            {
                if (transaction is not null) await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return Result.Failure<IDataExportSession>(ex);
            }
        }
        private sealed class Session(NpgsqlConnection connection, NpgsqlTransaction transaction, IDataReader reader, long count, DateTimeOffset cutoff) : IDataExportSession
        {
            public long Count => count;
            public DateTimeOffset Cutoff => cutoff;
            public IReadOnlyList<string> Columns { get; } = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var asyncReader = (System.Data.Common.DbDataReader)reader;
                while (await asyncReader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>();
                    for (var index = 0; index < reader.FieldCount; index++) row.Add(Columns[index], reader.IsDBNull(index) ? null : reader.GetValue(index));
                    yield return row;
                }
            }
            public async ValueTask DisposeAsync()
            {
                await ((System.Data.Common.DbDataReader)reader).DisposeAsync();
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
            }
        }
    }
}
