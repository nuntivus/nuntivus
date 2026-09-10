using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Matching;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Infastructure.Messaging.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace KairoServer.Worker.Matching
{
    /// <summary>
    /// Consumes <c>event.accepted</c> messages from JetStream via a durable pull
    /// consumer, invokes <see cref="IContractMatcherService"/>, and ACKs only
    /// after the matcher commits. Multiple replicas can run concurrently: they
    /// share the same durable, so JetStream distributes messages across them
    /// with server-side <c>MaxAckPending</c> flow control.
    /// </summary>
    public sealed class ContractMatcherWorker : BackgroundService
    {
        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _natsOptions;
        private readonly MatcherOption _matcherOptions;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAppLogger<ContractMatcherWorker> _logger;

        public ContractMatcherWorker(
            INatsJSContext jetStream,
            NatsOption natsOptions,
            MatcherOption matcherOptions,
            IServiceScopeFactory scopeFactory,
            IAppLogger<ContractMatcherWorker> logger)
        {
            _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
            _natsOptions = natsOptions ?? throw new ArgumentNullException(nameof(natsOptions));
            _matcherOptions = matcherOptions ?? throw new ArgumentNullException(nameof(matcherOptions));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "ContractMatcherWorker starting (stream {Stream}, durable {Durable}, filter {Filter}).",
                _natsOptions.Stream, _matcherOptions.DurableName, _matcherOptions.FilterSubject);

            var consumer = await CreateConsumerAsync(stoppingToken);

            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = _matcherOptions.BatchSize,
                Expires = _matcherOptions.FetchTimeout,
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var msg in consumer.FetchAsync<byte[]>(fetchOpts, cancellationToken: stoppingToken))
                    {
                        await ProcessMessageAsync(msg, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in ContractMatcherWorker fetch loop.");
                    // Prevent tight-loop on repeated failures.
                    try
                    {
                        await Task.Delay(_matcherOptions.NakDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.Information("ContractMatcherWorker stopped.");
        }

        private async Task<INatsJSConsumer> CreateConsumerAsync(CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig(_matcherOptions.DurableName)
            {
                FilterSubject = _matcherOptions.FilterSubject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                MaxAckPending = _matcherOptions.MaxAckPending,
                AckWait = TimeSpan.FromSeconds(30),
            };

            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _natsOptions.Stream, config, cancellationToken);

            _logger.Information(
                "JetStream durable consumer {Durable} ready on stream {Stream} (filter {Filter}).",
                _matcherOptions.DurableName, _natsOptions.Stream, _matcherOptions.FilterSubject);

            return consumer;
        }

        private async Task ProcessMessageAsync(NatsJSMsg<byte[]> msg, CancellationToken cancellationToken)
        {
            EventAccepted? accepted;
            try
            {
                var json = msg.Data is { Length: > 0 } bytes
                    ? Encoding.UTF8.GetString(bytes)
                    : null;

                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.Warning("Received empty event.accepted message; discarding (term).");
                    await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                    return;
                }

                accepted = JsonSerializer.Deserialize<EventAccepted>(json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize event.accepted payload; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                return;
            }

            if (accepted is null)
            {
                _logger.Warning("event.accepted deserialized to null; terminating message.");
                await msg.AckTerminateAsync(cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var matcher = scope.ServiceProvider.GetRequiredService<IContractMatcherService>();

                var result = await matcher.MatchAsync(accepted, cancellationToken);

                if (result.IsSuccess)
                {
                    await msg.AckAsync(cancellationToken: cancellationToken);
                }
                else
                {
                    _logger.Warning(
                        "Matcher failed for event {EventId}: {Error}. NAK for redelivery.",
                        accepted.EventId, result.Error);
                    await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Let JetStream redeliver on shutdown.
                try { await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: CancellationToken.None); }
                catch { /* best-effort */ }
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Unexpected error while processing event {EventId}. NAK for redelivery.",
                    accepted.EventId);
                try { await msg.NakAsync(delay: _matcherOptions.NakDelay, cancellationToken: cancellationToken); }
                catch { /* best-effort */ }
            }
        }
    }
}
