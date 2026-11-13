using NuntivusServer.Application.Persistence;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Infastructure.Messaging.Nats;
using NuntivusServer.Infastructure.Persistence.Postgres;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Microsoft.Extensions.Configuration;
using NuntivusServer.Common.Logging;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(MatcherCollection))]
    public sealed class WorkerHealthChecksTests
    {
        private readonly MatcherFixture _fixture;

        public WorkerHealthChecksTests(MatcherFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Postgres_and_required_tables_checks_are_healthy()
        {
            IDatabaseConnectionFactory factory = new PostgresDatabaseConnectionFactory(_fixture.ConnectionString);

            var postgresCheck = new PostgresConnectionHealthCheck(factory);
            var postgresResult = await postgresCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, postgresResult.Status);

            var tablesCheck = new PostgresRequiredTablesHealthCheck(
                factory,
                new RequiredTablesReadinessOption
                {
                    Schema = "public",
                    Tables = RequiredTablesReadinessDefaults.CoreTables,
                });

            var tablesResult = await tablesCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, tablesResult.Status);
        }

        [Fact]
        public async Task Required_tables_check_is_unhealthy_when_table_is_missing()
        {
            IDatabaseConnectionFactory factory = new PostgresDatabaseConnectionFactory(_fixture.ConnectionString);

            var tablesCheck = new PostgresRequiredTablesHealthCheck(
                factory,
                new RequiredTablesReadinessOption
                {
                    Schema = "public",
                    Tables = ["events", "table_does_not_exist"],
                });

            var result = await tablesCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("table_does_not_exist", result.Description);
        }

        [Fact]
        public async Task Nats_and_jetstream_checks_are_healthy()
        {
            var natsOption = new NatsOption
            {
                Url = _fixture.NatsUrl,
                Stream = "NUNTIVUS_HEALTH",
                Subjects = ["health.>"],
            };

            await using var connection = new NatsConnection(NatsOpts.Default with
            {
                Url = natsOption.Url,
                Name = "nuntivus-health-tests",
                ConnectTimeout = TimeSpan.FromSeconds(5),
            });

            var natsCheck = new NatsConnectionHealthCheck(connection);
            var natsResult = await natsCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, natsResult.Status);

            var js = new NatsJSContext(connection);
            await EnsureStreamAsync(js, natsOption);

            var jetStreamCheck = new JetStreamHealthCheck(js, natsOption);
            var jetStreamResult = await jetStreamCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, jetStreamResult.Status);
        }

        [Fact]
        public async Task Bootstrap_creates_and_updates_stream_when_binding_appends_duplicate_subjects()
        {
            var prefix = $"bootstrap-{Guid.NewGuid():N}";
            var options = new NatsOption { Subjects = [$"{prefix}.events.>", $"{prefix}.deliveries.>"] };
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Url"] = _fixture.NatsUrl,
                ["Stream"] = $"BOOTSTRAP_{Guid.NewGuid():N}",
                ["Subjects:0"] = options.Subjects[0],
                ["Subjects:1"] = options.Subjects[1],
            }).Build().Bind(options);
            Assert.Equal(4, options.Subjects.Length);
            await using var connection = new NatsConnection(NatsOpts.Default with { Url = options.Url });
            var js = new NatsJSContext(connection);
            var bootstrap = new NatsJetStreamBootstrapper(connection, js, options,
                new NullAppLogger<NatsJetStreamBootstrapper>());
            try
            {
                await bootstrap.StartAsync(CancellationToken.None);
                await bootstrap.StartAsync(CancellationToken.None);
                var stream = await js.GetStreamAsync(options.Stream);
                Assert.Equal(options.Subjects.Distinct(StringComparer.Ordinal), stream.Info.Config.Subjects);
            }
            finally
            {
                await js.DeleteStreamAsync(options.Stream);
            }
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }

        private static async Task EnsureStreamAsync(INatsJSContext js, NatsOption options)
        {
            var config = new StreamConfig(name: options.Stream, subjects: options.Subjects)
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                NumReplicas = 1,
                DuplicateWindow = options.DuplicateWindow,
            };

            try
            {
                await js.GetStreamAsync(options.Stream);
                await js.UpdateStreamAsync(config);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                await js.CreateStreamAsync(config);
            }
        }
    }
}
