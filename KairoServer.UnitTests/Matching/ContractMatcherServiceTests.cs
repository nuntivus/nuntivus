using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Matching;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.Events;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;
using KairoServer.Domain.Events;
using KairoServer.Domain.Outbox;

namespace KairoServer.UnitTests.Matching
{
    public sealed class ContractMatcherServiceTests
    {
        [Fact]
        public async Task Fails_when_filtered_contract_requires_payload_parse_and_payload_is_invalid_json()
        {
            var @event = Event.Rehydrate(
                Guid.NewGuid(),
                "idem-1",
                "orders.created",
                "source-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "not-json",
                metadata: null);

            var contractMatch = new ContractMatch(
                Guid.NewGuid(),
                "acme",
                new Destination(DestinationType.Http, "{\"Url\":\"https://hooks.example.com\"}"),
                new ContractFilter(
                    metadataRules: Array.Empty<ContractFilterRule>(),
                    payloadRules: new[]
                    {
                        new ContractFilterRule("order.amount", ContractFilterOperator.GreaterThan, "100", null)
                    }));

            var deliveries = new FakeDeliveryRepository();
            var matcher = new ContractMatcherService(
                new FakeEventRepository(@event),
                new FakeContractRepository(contractMatch),
                deliveries,
                new DeliveryFactory(),
                new FakeOutboxRepository(),
                new FakeUnitOfWork(),
                new ContractFilterEvaluator(),
                new NullLogger<ContractMatcherService>());

            var result = await matcher.MatchAsync(
                new EventAccepted(@event.Id, @event.Type, @event.SourceId, @event.OccurredAt),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("could not be parsed", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(deliveries.InsertCalled);
        }

        private sealed class FakeEventRepository : IEventRepository
        {
            private readonly Event _event;

            public FakeEventRepository(Event @event) => _event = @event;

            public Task<Result> InsertAsync(Event @event, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());

            public Task<Result<Event?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Event?>(_event));

            public Task<Result<Event?>> GetByIngestionKeyAsync(string sourceId, string idempotencyKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Event?>(_event));

            public Task<Result<EventReplay?>> GetReplayAsync(Guid sourceEventId, string replayKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<EventReplay?>(null));

            public Task<Result> InsertReplayAsync(EventReplay replay, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());
        }

        private sealed class FakeContractRepository : IContractRepository
        {
            private readonly IReadOnlyList<ContractMatch> _matches;

            public FakeContractRepository(params ContractMatch[] matches) => _matches = matches;

            public Task<Result> InsertAsync(Contract contract, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());

            public Task<Result> UpdateAsync(Contract contract, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());

            public Task<Result<Contract?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Contract?>(null));

            public Task<Result<PagedResult<Contract>>> ListByCustomerAsync(string customerId, int page, int pageSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(PagedResult<Contract>.Create(Array.Empty<Contract>(), page, pageSize, 0)));

            public Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(string eventType, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(_matches));
        }

        private sealed class FakeDeliveryRepository : IDeliveryRepository
        {
            public bool InsertCalled { get; private set; }

            public Task<Result<int>> InsertPendingAsync(IReadOnlyCollection<Delivery> deliveries, IUnitOfWork uow, CancellationToken cancellationToken)
            {
                InsertCalled = true;
                return Task.FromResult(Result.Success(deliveries.Count));
            }

            public Task<Result<Delivery?>> GetByIdAsync(Guid deliveryId, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Delivery?>(null));

            public Task<Result<bool>> UpdateAsync(Delivery delivery, DateTimeOffset expectedUpdatedAt, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(true));

            public Task<Result<int>> AddAttemptAsync(DeliveryAttempt attempt, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(1));

            public Task<Result<IReadOnlyList<DeliveryAttempt>>> GetAttemptsByDeliveryIdAsync(Guid deliveryId, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<IReadOnlyList<DeliveryAttempt>>(Array.Empty<DeliveryAttempt>()));

            public Task<Result<PagedResult<Delivery>>> ListDeadAsync(int page, int pageSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(PagedResult<Delivery>.Create(Array.Empty<Delivery>(), page, pageSize, 0)));

            public Task<Result<DeliveryRateLimitPermit>> TryAcquireRateLimitPermitAsync(Guid contractId, ContractRateLimit rateLimit, DateTimeOffset now, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(DeliveryRateLimitPermit.Allow()));

            public Task<Result<bool>> ScheduleNextAttemptAsync(Guid deliveryId, DateTimeOffset expectedUpdatedAt, DateTimeOffset nextAttemptAt, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(true));
        }

        private sealed class FakeOutboxRepository : IOutboxRepository
        {
            public Task<Result> InsertAsync(OutboxMessage message, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());
        }

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            public IDbConnection? GetConnection() => null;
            public IDbTransaction? GetTransaction() => null;
            public void Begin(bool inTransaction = true) { }
            public void Commit() { }
            public void Rollback() { }
        }

        private sealed class NullLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }
}
