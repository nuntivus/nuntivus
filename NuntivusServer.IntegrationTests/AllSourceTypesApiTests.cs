using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using NuntivusServer.Contracts.Sources;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.IntegrationTests;

[Collection(nameof(EventsApiCollection))]
public sealed class AllSourceTypesApiTests : IAsyncLifetime
{
    private readonly EventsApiFixture _fixture;
    private readonly HttpClient _client;
    public AllSourceTypesApiTests(EventsApiFixture fixture)
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
        yield return ["HTTP_API", "{\"authentication\":{\"type\":\"none\"},\"method\":\"POST\"}"];
        yield return ["RABBITMQ", "{\"uri\":\"amqps://broker.example.com\",\"queue\":\"events\"}"];
        yield return ["KAFKA", "{\"bootstrapServers\":\"broker:9093\",\"topic\":\"events\",\"groupId\":\"ingestion\"}"];
        yield return ["SQS", "{\"region\":\"us-east-1\",\"queueUrl\":\"https://sqs.us-east-1.amazonaws.com/123/events\"}"];
        yield return ["NATS", "{\"url\":\"tls://broker.example.com:4222\",\"stream\":\"EXTERNAL\",\"subject\":\"events\",\"durable\":\"ingestion\"}"];
        yield return ["HTTP_POLLING", "{\"url\":\"https://example.com/events\",\"intervalSeconds\":30}"];
        yield return ["FOLDER", JsonSerializer.Serialize(new { path = Path.GetTempPath(), intervalSeconds = 30 })];
        yield return ["S3", "{\"region\":\"us-east-1\",\"bucket\":\"events\",\"prefix\":\"incoming/\"}"];
    }
    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task Each_supported_type_round_trips_updates_disables_enables_and_deletes(string type, string json)
    {
        var configuration = JsonSerializer.Deserialize<JsonElement>(json);
        var created = await _client.PostAsJsonAsync("/v1/sources", new { name = $"{type} source", type, configuration });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var source = (await created.Content.ReadFromJsonAsync<SourceView>())!;
        Assert.Equal(type, source.Type);
        Assert.True(source.Enabled);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        var stored = await connection.QuerySingleAsync<(string Type, string Configuration)>(
            "SELECT type, configuration::text FROM sources WHERE id = @Id", new { Id = Guid.Parse(source.Id) });
        Assert.Equal(type, stored.Type);
        using var persisted = JsonDocument.Parse(stored.Configuration);
        foreach (var field in configuration.EnumerateObject())
            Assert.True(persisted.RootElement.TryGetProperty(field.Name, out _), field.Name);
        var detail = (await _client.GetFromJsonAsync<SourceView>($"/v1/sources/{source.Id}"))!;
        Assert.Equal(source.Type, detail.Type);
        var update = await _client.PutAsJsonAsync($"/v1/sources/{source.Id}", new { name = "Updated source", configuration });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Updated source", (await update.Content.ReadFromJsonAsync<SourceView>())!.Name);
        var invalid = await _client.PutAsJsonAsync($"/v1/sources/{source.Id}", new { name = "Invalid", configuration = new { password = "forbidden-inline" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Updated source", (await _client.GetFromJsonAsync<SourceView>($"/v1/sources/{source.Id}"))!.Name);
        Assert.False((await (await _client.PostAsync($"/v1/sources/{source.Id}/disable", null)).Content.ReadFromJsonAsync<SourceView>())!.Enabled);
        Assert.True((await (await _client.PostAsync($"/v1/sources/{source.Id}/enable", null)).Content.ReadFromJsonAsync<SourceView>())!.Enabled);
        if (type != "HTTP_API")
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/v1/sources/{source.Id}/events", new { payload = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/v1/sources/{source.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/v1/sources/{source.Id}")).StatusCode);
    }
    [Theory]
    [InlineData("GENERIC_WEBHOOK")]
    [InlineData("SDK_APPLICATION")]
    [InlineData("STRIPE_WEBHOOK_INTEGRATION")]
    public async Task Unsupported_types_cannot_be_registered(string type)
    {
        var response = await _client.PostAsJsonAsync("/v1/sources", new { name = "Unsupported", type, configuration = new { } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public void Fixtures_account_for_every_declared_source_type()
    {
        var covered = Configurations().Select(row => (string)row[0]).Concat(["GENERIC_WEBHOOK", "SDK_APPLICATION", "STRIPE_WEBHOOK_INTEGRATION"]);
        Assert.Equal(Enum.GetNames<SourceType>().Order(), covered.Order());
    }
}
