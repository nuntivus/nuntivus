using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Dashboard;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Contracts.Dashboard;
using Npgsql;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDashboardTimeSeriesQueryService : IDashboardTimeSeriesQueryService
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PostgresDashboardTimeSeriesQueryService(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Result<DashboardTimeSeriesView>> GetAsync(int rangeSeconds, CancellationToken cancellationToken)
        {
            if (rangeSeconds is not (15 or 30 or 60 or 3600))
                return Result.Failure<DashboardTimeSeriesView>("validation", "rangeSeconds must be 15, 30, 60, or 3600.");

            try
            {
                var asOf = DateTimeOffset.UtcNow;
                var start = asOf.AddSeconds(-rangeSeconds);
                var bucketCount = rangeSeconds is 15 or 30 ? 15 : 20;
                var bucketSeconds = rangeSeconds / bucketCount;

                const string sql = """
                    WITH event_counts AS (
                        SELECT FLOOR(EXTRACT(EPOCH FROM (received_at - @Start)) / @BucketSeconds)::int AS bucket,
                               COUNT(*) AS events
                        FROM events
                        WHERE received_at >= @Start AND received_at < @AsOf
                        GROUP BY 1
                    ), delivery_counts AS (
                        SELECT FLOOR(EXTRACT(EPOCH FROM (updated_at - @Start)) / @BucketSeconds)::int AS bucket,
                               COUNT(*) AS deliveries,
                               COUNT(*) FILTER (WHERE status = 'Delivered') AS delivered,
                               COUNT(*) FILTER (WHERE status = 'Failed') AS failed,
                               COUNT(*) FILTER (WHERE status = 'Dead') AS dead
                        FROM deliveries
                        WHERE updated_at >= @Start AND updated_at < @AsOf
                        GROUP BY 1
                    )
                    SELECT @Start + (b.bucket * @BucketSeconds) * INTERVAL '1 second' AS Start,
                           COALESCE(e.events, 0) AS Events,
                           COALESCE(d.deliveries, 0) AS Deliveries,
                           COALESCE(d.delivered, 0) AS Delivered,
                           COALESCE(d.failed, 0) AS Failed,
                           COALESCE(d.dead, 0) AS Dead
                    FROM generate_series(0, @BucketCount - 1) AS b(bucket)
                    LEFT JOIN event_counts e ON e.bucket = b.bucket
                    LEFT JOIN delivery_counts d ON d.bucket = b.bucket
                    ORDER BY b.bucket;
                    """;

                await using var connection = (NpgsqlConnection)_connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var rows = await connection.QueryAsync<BucketRow>(new CommandDefinition(
                    sql,
                    new { Start = start, AsOf = asOf, BucketSeconds = bucketSeconds, BucketCount = bucketCount },
                    cancellationToken: cancellationToken));

                var buckets = rows.Select(static row => new DashboardTimeBucketView(
                    row.Start, row.Events, row.Deliveries, row.Delivered, row.Failed, row.Dead)).ToArray();
                return Result.Success(new DashboardTimeSeriesView(asOf, rangeSeconds, buckets));
            }
            catch (Exception ex)
            {
                return Result.Failure<DashboardTimeSeriesView>(ex);
            }
        }

        private sealed class BucketRow
        {
            public DateTimeOffset Start { get; set; }
            public long Events { get; set; }
            public long Deliveries { get; set; }
            public long Delivered { get; set; }
            public long Failed { get; set; }
            public long Dead { get; set; }
        }
    }
}
