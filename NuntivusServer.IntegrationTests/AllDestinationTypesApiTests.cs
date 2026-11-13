using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Contracts.Destinations;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.IntegrationTests;

[Collection(nameof(ContractsApiCollection))]
public sealed class AllDestinationTypesApiTests : IAsyncLifetime
{
    private readonly ContractsApiFixture _fixture;
    private readonly HttpClient _client;
    public AllDestinationTypesApiTests(ContractsApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
        _client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
    }
    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }
    public static IEnumerable<object[]> Configurations()
    {
        yield return [DestinationType.Http, new HttpDestinationConfig("https://example.com/events", new HttpAuthentication("bearer", new HttpCredential("inline", "api-fixture-credential")))];
        yield return [DestinationType.Kafka, new KafkaDestinationConfig("broker:9093", "events", Credentials: new KafkaCredentials(Password: new DestinationCredential("inline", "api-fixture-credential")))];
        yield return [DestinationType.Nats, new NatsDestinationConfig("tls://broker.example.com:4222", "events", Credentials: new NatsCredentials(Token: new DestinationCredential("inline", "api-fixture-credential")))];
        yield return [DestinationType.RabbitMq, new RabbitMqDestinationConfig("amqps://broker.example.com", "events", "events.created", Credentials: new RabbitMqCredentials(Password: new DestinationCredential("inline", "api-fixture-credential")))];
        yield return [DestinationType.Sqs, new SqsDestinationConfig("us-east-1", "https://sqs.us-east-1.amazonaws.com/123/events", Credentials: new SqsCredentials("fixture-id", new DestinationCredential("inline", "api-fixture-credential")))];
    }
    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task Each_type_persists_protects_credentials_and_supports_lifecycle(DestinationType type, DestinationConfig configuration)
    {
        var response = await _client.PostAsJsonAsync("/v1/destinations", new { name = $"{type} destination", configuration });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var destination = (await response.Content.ReadFromJsonAsync<DestinationResourceView>())!;
        Assert.DoesNotContain("api-fixture-credential", await response.Content.ReadAsStringAsync());
        Assert.True(destination.Configured);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        var stored = await connection.QuerySingleAsync<(string Type, string Configuration)>(
            "SELECT type, configuration::text FROM destinations WHERE id = @Id", new { destination.Id });
        Assert.Equal(type.ToString(), stored.Type);
        Assert.DoesNotContain("api-fixture-credential", stored.Configuration);
        Assert.Contains("protected.", stored.Configuration);
        var detail = (await _client.GetFromJsonAsync<DestinationResourceView>($"/v1/destinations/{destination.Id}"))!;
        Assert.Equal(destination.Type, detail.Type);
        var updated = await _client.PutAsJsonAsync($"/v1/destinations/{destination.Id}", new { name = "Updated destination", configuration });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.DoesNotContain("api-fixture-credential", await updated.Content.ReadAsStringAsync());
        var disabled = await _client.PostAsJsonAsync($"/v1/destinations/{destination.Id}/disable", new { pauseExistingBindings = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await disabled.Content.ReadFromJsonAsync<DestinationResourceView>())!.Enabled);
        var enabled = await _client.PostAsync($"/v1/destinations/{destination.Id}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.True((await enabled.Content.ReadFromJsonAsync<DestinationResourceView>())!.Enabled);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/v1/destinations/{destination.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/v1/destinations/{destination.Id}")).StatusCode);
    }
}
