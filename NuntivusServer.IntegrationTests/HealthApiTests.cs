using System.Net;
using Dapper;
using Npgsql;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(CustomersApiCollection))]
    public sealed class HealthApiTests
    {
        private readonly CustomersApiFixture _fixture;
        private readonly HttpClient _client;

        public HealthApiTests(CustomersApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
        }

        [Fact]
        public async Task Live_returns_200()
        {
            var response = await _client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Ready_returns_503_when_required_tables_are_missing()
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.ExecuteAsync("ALTER TABLE sources RENAME TO sources_not_ready;");
            try
            {
                var response = await _client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            }
            finally
            {
                await conn.ExecuteAsync("ALTER TABLE sources_not_ready RENAME TO sources;");
            }
        }
    }

    [Collection(nameof(ContractsApiCollection))]
    public sealed class HealthApiReadinessTests
    {
        private readonly HttpClient _client;

        public HealthApiReadinessTests(ContractsApiFixture fixture)
        {
            _client = fixture.CreateClient();
        }

        [Fact]
        public async Task Ready_returns_200_when_core_schema_is_present()
        {
            var response = await _client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
