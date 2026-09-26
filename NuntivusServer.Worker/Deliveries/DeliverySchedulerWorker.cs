using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Deliveries;
using NuntivusServer.Domain.Outbox;
using NuntivusServer.Worker.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NuntivusServer.Worker.Deliveries
{
    public sealed class DeliverySchedulerWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DeliverySchedulerOption _option;
        private readonly IAppLogger<DeliverySchedulerWorker> _logger;

        public DeliverySchedulerWorker(
            IServiceScopeFactory scopeFactory,
            DeliverySchedulerOption option,
            IAppLogger<DeliverySchedulerWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _option = option ?? throw new ArgumentNullException(nameof(option));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                "DeliverySchedulerWorker started (batch {BatchSize}, poll {PollDelay}, empty poll {EmptyPollDelay}).",
                _option.BatchSize,
                _option.PollDelay,
                _option.EmptyPollDelay);

            while (!stoppingToken.IsCancellationRequested)
            {
                var scheduled = 0;
                try
                {
                    scheduled = await ScheduleDueDeliveriesAsync(stoppingToken);
                    WorkerMetrics.TrackDeliverySchedulerOutcome(scheduled > 0 ? "scheduled" : "idle");
                    WorkerMetrics.TrackWorkerProcessed("delivery-scheduler", scheduled > 0 ? "scheduled" : "idle");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "DeliverySchedulerWorker iteration failed.");
                    WorkerMetrics.TrackDeliverySchedulerOutcome("error");
                    WorkerMetrics.TrackWorkerProcessed("delivery-scheduler", "error");
                }

                try
                {
                    var delay = scheduled == 0 ? _option.EmptyPollDelay : _option.PollDelay;
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.Information("DeliverySchedulerWorker stopped.");
        }

        private async Task<int> ScheduleDueDeliveriesAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var now = DateTimeOffset.UtcNow;
            unitOfWork.Begin(true);
            try
            {
                var dueResult = await deliveries.ClaimDueForSchedulingAsync(
                    now,
                    _option.BatchSize,
                    unitOfWork,
                    cancellationToken);
                dueResult.EnsureSuccess();

                var dueDeliveries = dueResult.Value;
                foreach (var delivery in dueDeliveries)
                {
                    var ready = new DeliveryReady(
                        delivery.Id,
                        delivery.EventId,
                        delivery.ContractId,
                        delivery.CustomerId,
                        delivery.EventType,
                        delivery.Payload,
                        delivery.CreatedAt);

                    var outboxMessage = OutboxMessage.Create(
                        delivery.EventId,
                        OutboxMessageType.DeliveryReady,
                        JsonSerializer.Serialize(ready),
                        now);

                    (await outbox.InsertAsync(outboxMessage, unitOfWork, cancellationToken)).EnsureSuccess();
                }

                unitOfWork.Commit();

                if (dueDeliveries.Count > 0)
                {
                    WorkerMetrics.TrackDeliverySchedulerClaimed(dueDeliveries.Count);
                    _logger.Information(
                        "Scheduled {Count} due delivery retry message(s).",
                        dueDeliveries.Count);
                }

                return dueDeliveries.Count;
            }
            catch
            {
                unitOfWork.Rollback();
                throw;
            }
        }
    }
}
