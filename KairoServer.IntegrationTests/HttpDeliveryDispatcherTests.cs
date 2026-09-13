using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Persistence;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;
using KairoServer.Infastructure.Deliveries.Http;
using KairoServer.Infastructure.Persistence.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    /// <summary>
    /// End-to-end verification of the HTTP delivery destination. Spins up an
    /// in-process ASP.NET Core fake server and drives the
    /// <see cref="DeliveryDispatcherService"/> against a real Postgres to
    /// assert the full round-trip: headers, retry classification, terminal
    /// failure, DeliveryAttempt rows.
    /// </summary>
    [Collection(nameof(DeliveriesCollection))]
    public sealed class HttpDeliveryDispatcherTests : IAsyncLifetime
    {
        private readonly DeliveriesFixture _fx;
        private FakeHttpServer _server = null!;

        public HttpDeliveryDispatcherTests(DeliveriesFixture fx) => _fx = fx;

        public async Task InitializeAsync()
        {
            await _fx.ResetAsync();
            _server = await FakeHttpServer.StartAsync();
        }

        public async Task DisposeAsync() => await _server.DisposeAsync();

        [Fact]
        public async Task Dispatch_success_marks_delivered_and_records_attempt_with_headers()
        {
            var (deliveryId, eventId) = await SeedDeliveryAsync(_server.Url);
            _server.Enqueue(Respond(HttpStatusCode.OK, "{\"ok\":true}"));

            var dispatcher = BuildDispatcher();
            var result = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            result.EnsureSuccess();

            Assert.Equal(DeliveryDispatchOutcome.Delivered, result.Value);

            var (status, attemptCount, lastError, nextAttemptAt) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal("Delivered", status);
            Assert.Equal(1, attemptCount);
            Assert.Null(lastError);
            Assert.Null(nextAttemptAt);

            var attempts = await ReadAttemptsAsync(deliveryId);
            var attempt = Assert.Single(attempts);
            Assert.True(attempt.succeeded);
            Assert.Equal("200", attempt.HttpStatus);
            Assert.Equal(1, attempt.attempt_number);

            var received = Assert.Single(_server.Received);
            Assert.Equal("POST", received.Method);
            Assert.Equal(deliveryId.ToString(), received.Headers["X-Kairo-Delivery-Id"]);
            Assert.Equal(eventId.ToString(), received.Headers["X-Kairo-Event-Id"]);
            Assert.Equal("orders.created", received.Headers["X-Kairo-Event-Type"]);
            Assert.Contains("\"orderId\"", received.Body);
            Assert.Equal("application/json", received.ContentType?.Split(';')[0]);
        }

        [Fact]
        public async Task Dispatch_transient_5xx_schedules_retry_and_records_status()
        {
            var (deliveryId, _) = await SeedDeliveryAsync(_server.Url);
            _server.Enqueue(Respond(HttpStatusCode.InternalServerError, "boom"));

            var dispatcher = BuildDispatcher();
            var result = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            result.EnsureSuccess();

            Assert.Equal(DeliveryDispatchOutcome.FailedRetry, result.Value);

            var (status, attemptCount, lastError, nextAttemptAt) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal("Failed", status);
            Assert.Equal(1, attemptCount);
            Assert.NotNull(lastError);
            Assert.Contains("500", lastError);
            Assert.NotNull(nextAttemptAt);

            var attempt = Assert.Single(await ReadAttemptsAsync(deliveryId));
            Assert.False(attempt.succeeded);
            Assert.Equal("500", attempt.HttpStatus);
            Assert.Equal("Transient", attempt.failure_category);
            Assert.Contains("boom", attempt.ResponseBody);
        }

        [Fact]
        public async Task Dispatch_permanent_4xx_marks_terminal_no_retry()
        {
            var (deliveryId, _) = await SeedDeliveryAsync(_server.Url);
            _server.Enqueue(Respond(HttpStatusCode.BadRequest, "nope"));

            var dispatcher = BuildDispatcher();
            var result = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            result.EnsureSuccess();

            Assert.Equal(DeliveryDispatchOutcome.FailedTerminal, result.Value);

            var (status, _, _, nextAttemptAt) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal("Dead", status);
            Assert.Null(nextAttemptAt);

            var attempt = Assert.Single(await ReadAttemptsAsync(deliveryId));
            Assert.Equal("Permanent", attempt.failure_category);
            Assert.Equal("400", attempt.HttpStatus);
        }

        [Fact]
        public async Task Dispatch_429_respects_retry_after_when_scheduling_retry()
        {
            var (deliveryId, _) = await SeedDeliveryAsync(_server.Url);
            _server.Enqueue(RespondWithRetryAfter(HttpStatusCode.TooManyRequests, "slow down", TimeSpan.FromSeconds(3)));

            var dispatcher = BuildDispatcher(new DeliveryDispatcherOption
            {
                MaxAttempts = 5,
                BaseBackoff = TimeSpan.FromMilliseconds(10),
                MaxBackoff = TimeSpan.FromSeconds(10),
                JitterRatio = 0,
            });

            var started = DateTimeOffset.UtcNow;
            var result = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            result.EnsureSuccess();

            Assert.Equal(DeliveryDispatchOutcome.FailedRetry, result.Value);

            var (_, _, _, nextAttemptAt) = await ReadDeliveryAsync(deliveryId);
            Assert.NotNull(nextAttemptAt);
            Assert.InRange(nextAttemptAt!.Value - started, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));

            var attempt = Assert.Single(await ReadAttemptsAsync(deliveryId));
            Assert.Equal("Throttled", attempt.failure_category);
            Assert.Equal("429", attempt.HttpStatus);
        }

        [Fact]
        public async Task Dispatch_dns_or_connection_error_classified_transient()
        {
            // Use a URL that will never resolve to force a transport error.
            var (deliveryId, _) = await SeedDeliveryAsync("http://kairo-does-not-exist.invalid/hook");

            var dispatcher = BuildDispatcher(new DeliveryDispatcherOption
            {
                MaxAttempts = 5,
                BaseBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(1),
            });
            var result = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            result.EnsureSuccess();

            Assert.Equal(DeliveryDispatchOutcome.FailedRetry, result.Value);

            var attempt = Assert.Single(await ReadAttemptsAsync(deliveryId));
            Assert.False(attempt.succeeded);
            Assert.Equal("Transient", attempt.failure_category);
            Assert.Null(attempt.HttpStatus);
        }

        [Fact]
        public async Task Dispatch_exhausts_retries_becomes_terminal_after_max_attempts()
        {
            var (deliveryId, _) = await SeedDeliveryAsync(_server.Url);

            var options = new DeliveryDispatcherOption
            {
                MaxAttempts = 2,
                BaseBackoff = TimeSpan.FromMilliseconds(1),
                MaxBackoff = TimeSpan.FromMilliseconds(1),
            };
            var dispatcher = BuildDispatcher(options);

            _server.Enqueue(Respond(HttpStatusCode.ServiceUnavailable, "try later"));
            var r1 = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            r1.EnsureSuccess();
            Assert.Equal(DeliveryDispatchOutcome.FailedRetry, r1.Value);

            _server.Enqueue(Respond(HttpStatusCode.ServiceUnavailable, "still bad"));
            var r2 = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            r2.EnsureSuccess();
            Assert.Equal(DeliveryDispatchOutcome.FailedTerminal, r2.Value);

            var (status, attemptCount, _, nextAttemptAt) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal("Dead", status);
            Assert.Equal(2, attemptCount);
            Assert.Null(nextAttemptAt);

            var attempts = await ReadAttemptsAsync(deliveryId);
            Assert.Equal(2, attempts.Count);
        }

        [Fact]
        public async Task Dispatch_before_next_attempt_due_is_skipped_without_new_attempt()
        {
            var (deliveryId, _) = await SeedDeliveryAsync(_server.Url);

            var options = new DeliveryDispatcherOption
            {
                MaxAttempts = 5,
                BaseBackoff = TimeSpan.FromSeconds(30),
                MaxBackoff = TimeSpan.FromSeconds(30),
                JitterRatio = 0,
            };
            var dispatcher = BuildDispatcher(options);

            _server.Enqueue(Respond(HttpStatusCode.ServiceUnavailable, "retry later"));
            var first = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            first.EnsureSuccess();
            Assert.Equal(DeliveryDispatchOutcome.FailedRetry, first.Value);

            var (_, attemptCountBefore, _, nextAttemptAtBefore) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal(1, attemptCountBefore);
            Assert.NotNull(nextAttemptAtBefore);

            _server.Enqueue(Respond(HttpStatusCode.OK, "should-not-run-yet"));
            var second = await dispatcher.DispatchAsync(deliveryId, CancellationToken.None);
            second.EnsureSuccess();
            Assert.Equal(DeliveryDispatchOutcome.Skipped, second.Value);

            var (_, attemptCountAfter, _, nextAttemptAtAfter) = await ReadDeliveryAsync(deliveryId);
            Assert.Equal(1, attemptCountAfter);
            Assert.Equal(nextAttemptAtBefore, nextAttemptAtAfter);

            var attempts = await ReadAttemptsAsync(deliveryId);
            Assert.Single(attempts);
            Assert.Single(_server.Received);
        }

        // ---- helpers ---------------------------------------------------------

        private IDeliveryDispatcherService BuildDispatcher(DeliveryDispatcherOption? options = null)
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());
            services.AddSingleton(typeof(IAppLogger<>), typeof(NullAppLogger<>));

            services.AddSingleton<IDatabaseConnectionFactory>(
                new PostgresDatabaseConnectionFactory(_fx.ConnectionString));
            services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
            services.AddScoped<IDeliveryRepository, PostgresDeliveryRepository>();

            services.AddHttpClient(HttpDeliveryExecutor.HttpClientName);
            services.AddSingleton<ISecretResolver, NullSecretResolver>();
            services.AddSingleton<IDeliveryExecutor, HttpDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutorDispatcher, DeliveryExecutorDispatcher>();

            services.AddSingleton(options ?? new DeliveryDispatcherOption
            {
                MaxAttempts = 5,
                BaseBackoff = TimeSpan.FromMilliseconds(1),
                MaxBackoff = TimeSpan.FromMilliseconds(1),
            });
            services.AddScoped<IDeliveryDispatcherService, DeliveryDispatcherService>();

            var sp = services.BuildServiceProvider();
            return sp.CreateScope().ServiceProvider.GetRequiredService<IDeliveryDispatcherService>();
        }

        private async Task<(Guid deliveryId, Guid eventId)> SeedDeliveryAsync(string url)
        {
            var customerId = "cust-" + Guid.NewGuid().ToString("N")[..6];
            var contractId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO customers(id, name, enabled, created_at, updated_at) VALUES (@id, @id, TRUE, @now, @now);",
                new { id = customerId, now });
            await conn.ExecuteAsync("""
                INSERT INTO contracts(id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@id, @cid, TRUE, 'Http', @cfg::jsonb, @now, @now);
                """,
                new { id = contractId, cid = customerId, cfg = JsonSerializer.Serialize(new HttpDestinationConfig(
                    url,
                    new HttpAuthentication("none"),
                    Headers: null,
                    TimeoutSeconds: 5)), now });
            await conn.ExecuteAsync("""
                INSERT INTO events(id, type, idempotency_key, source_id, occurred_at, received_at, metadata, payload)
                VALUES (@id, 'orders.created', @idem, @src, @now, @now, '{}'::jsonb, '{}'::jsonb);
                """,
                new { id = eventId, idem = Guid.NewGuid().ToString("N"), src = Guid.NewGuid(), now });

            var destination = new Destination(KairoServer.Domain.Contracts.DestinationType.Http, JsonSerializer.Serialize(new HttpDestinationConfig(
                url,
                new HttpAuthentication("none"),
                Headers: null,
                TimeoutSeconds: 5)));

            var delivery = Delivery.CreatePending(
                eventId, contractId, customerId, "orders.created",
                "{\"orderId\":\"o-1\"}", destination, now);

            var factory = new PostgresDatabaseConnectionFactory(_fx.ConnectionString);
            using var uow = new PostgresUnitOfWork(factory);
            uow.Begin(true);
            var repo = new PostgresDeliveryRepository(factory);
            (await repo.InsertPendingAsync(new[] { delivery }, uow, CancellationToken.None)).EnsureSuccess();
            uow.Commit();

            return (delivery.Id, eventId);
        }

        private async Task<(string status, int attempt_count, string? last_error, DateTimeOffset? next_attempt_at)>
            ReadDeliveryAsync(Guid id)
        {
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            return await conn.QuerySingleAsync<(string, int, string?, DateTimeOffset?)>(
                "SELECT status, attempt_count, last_error, next_attempt_at FROM deliveries WHERE id = @id;",
                new { id });
        }

        private async Task<List<AttemptRow>> ReadAttemptsAsync(Guid deliveryId)
        {
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            var rows = await conn.QueryAsync<AttemptRow>(
                "SELECT attempt_number, succeeded, failure_category, error, metadata FROM delivery_attempts WHERE delivery_id = @id ORDER BY attempt_number;",
                new { id = deliveryId });
            return rows.ToList();
        }

        private static Func<HttpContext, Task> Respond(HttpStatusCode status, string body) => async ctx =>
        {
            ctx.Response.StatusCode = (int)status;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(body);
        };

        private static Func<HttpContext, Task> RespondWithRetryAfter(
            HttpStatusCode status,
            string body,
            TimeSpan retryAfter) => async ctx =>
        {
            ctx.Response.StatusCode = (int)status;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
            await ctx.Response.WriteAsync(body);
        };

        public sealed class AttemptRow
        {
            public int attempt_number { get; set; }
            public bool succeeded { get; set; }
            public string? failure_category { get; set; }
            public string? error { get; set; }
            public string? metadata { get; set; }

            public string? HttpStatus
            {
                get
                {
                    if (string.IsNullOrEmpty(metadata)) return null;
                    using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                    return doc.RootElement.TryGetProperty("httpStatus", out var v) ? v.GetString() : null;
                }
            }

            public string? ResponseBody
            {
                get
                {
                    if (string.IsNullOrEmpty(metadata)) return null;
                    using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                    return doc.RootElement.TryGetProperty("responseBody", out var v) ? v.GetString() : null;
                }
            }
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string m, params object?[] a) { }
            public void Information(string m, params object?[] a) { }
            public void Warning(string m, params object?[] a) { }
            public void Error(string m, params object?[] a) { }
            public void Error(Exception ex, string m, params object?[] a) { }
            public void Fatal(string m, params object?[] a) { }
            public void Fatal(Exception ex, string m, params object?[] a) { }
        }
    }

    /// <summary>
    /// Minimal in-process HTTP server backed by Kestrel. Tests enqueue
    /// per-request response builders and inspect received requests.
    /// </summary>
    internal sealed class FakeHttpServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<Func<HttpContext, Task>> _handlers = new();
        private readonly ConcurrentQueue<ReceivedRequest> _received = new();

        public string Url { get; }
        public IReadOnlyCollection<ReceivedRequest> Received => _received.ToArray();

        private FakeHttpServer(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public static async Task<FakeHttpServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();

            FakeHttpServer? server = null;

            app.MapPost("/hook", async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var headers = ctx.Request.Headers.ToDictionary(
                    h => h.Key, h => (string)h.Value!, StringComparer.OrdinalIgnoreCase);
                server!._received.Enqueue(new ReceivedRequest(
                    ctx.Request.Method, headers, body, ctx.Request.ContentType));

                if (server._handlers.TryDequeue(out var handler))
                {
                    await handler(ctx);
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                }
            });

            await app.StartAsync();
            var url = app.Urls.First() + "/hook";
            server = new FakeHttpServer(app, url);
            return server;
        }

        public void Enqueue(Func<HttpContext, Task> handler) => _handlers.Enqueue(handler);

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        public sealed record ReceivedRequest(
            string Method,
            Dictionary<string, string> Headers,
            string Body,
            string? ContentType);
    }
}
