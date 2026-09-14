using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Events;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(EventsApiCollection))]
    public sealed class EventsApiTests : IAsyncLifetime
    {
        private readonly EventsApiFixture _fixture;
        private readonly HttpClient _client;

        public EventsApiTests(EventsApiFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClient();
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Replay_existing_event_creates_new_event_and_tracks_lineage()
        {
            var sourceId = await SeedEventAsync();

            var resp = await _client.PostAsJsonAsync(
                $"/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-1", "manual replay"));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ReplayEventResponse>();
            Assert.NotNull(body);
            Assert.Equal("accepted", body!.Status);
            Assert.Equal(sourceId.ToString("D"), body.SourceEventId);

            var replayedEventId = Guid.Parse(body.ReplayedEventId);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var replayRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_replays WHERE source_event_id = @source AND replayed_event_id = @replayed;",
                new { source = sourceId, replayed = replayedEventId });
            Assert.Equal(1, replayRows);

            var outboxRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM outbox_messages WHERE event_id = @eventId AND type = 'EventAccepted';",
                new { eventId = replayedEventId });
            Assert.Equal(1, outboxRows);
        }

        [Fact]
        public async Task Replay_same_key_is_idempotent_and_does_not_duplicate()
        {
            var sourceId = await SeedEventAsync();

            var first = await _client.PostAsJsonAsync(
                $"/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-2", null));
            var second = await _client.PostAsJsonAsync(
                $"/events/{sourceId:D}/replay",
                new ReplayEventRequest("ops-replay-2", null));

            var firstBody = await first.Content.ReadFromJsonAsync<ReplayEventResponse>();
            var secondBody = await second.Content.ReadFromJsonAsync<ReplayEventResponse>();

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("accepted", firstBody!.Status);
            Assert.Equal("already_replayed", secondBody!.Status);
            Assert.Equal(firstBody.ReplayedEventId, secondBody.ReplayedEventId);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var replayRows = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM event_replays WHERE source_event_id = @source AND replay_key = @key;",
                new { source = sourceId, key = "ops-replay-2" });
            Assert.Equal(1, replayRows);
        }

        [Fact]
        public async Task Replay_missing_event_returns_404()
        {
            var resp = await _client.PostAsJsonAsync(
                $"/events/{Guid.NewGuid():D}/replay",
                new ReplayEventRequest("ops-replay-missing", null));

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.Equal("not_found", err!.Code);
        }

        private async Task<Guid> SeedEventAsync()
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(
                """
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @source, @now, @now, '{}'::jsonb, '{"orderId":"o-1"}'::jsonb);
                """,
                new
                {
                    id,
                    idem = "seed-" + id,
                    source = Guid.NewGuid(),
                    now
                });

            return id;
        }
    }
}