using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Domain.Contracts;

namespace KairoServer.UnitTests.Deliveries.Execution
{
    public class DeliveryExecutorDispatcherTests
    {
        private static DeliveryExecutionRequest NewRequest(DestinationType type) =>
            new(
                DeliveryId: Guid.NewGuid(),
                EventId: Guid.NewGuid(),
                ContractId: Guid.NewGuid(),
                CustomerId: "acme",
                EventType: "orders.created",
                Payload: "{}",
                AttemptNumber: 1,
                Destination: new Destination(type, "{}"));

        [Fact]
        public async Task Forwards_to_executor_matching_destination_type()
        {
            var stub = new StubExecutor(DestinationType.Http, DeliveryExecutionResult.Success());
            var dispatcher = new DeliveryExecutorDispatcher(new IDeliveryExecutor[] { stub });

            var result = await dispatcher.ExecuteAsync(NewRequest(DestinationType.Http), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(1, stub.Calls);
        }

        [Fact]
        public async Task Returns_configuration_invalid_when_no_executor_registered()
        {
            var dispatcher = new DeliveryExecutorDispatcher(Array.Empty<IDeliveryExecutor>());

            var result = await dispatcher.ExecuteAsync(NewRequest(DestinationType.Http), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(DeliveryFailureCategory.ConfigurationInvalid, result.Category);
        }

        [Fact]
        public void Throws_when_multiple_executors_registered_for_same_type()
        {
            var a = new StubExecutor(DestinationType.Http, DeliveryExecutionResult.Success());
            var b = new StubExecutor(DestinationType.Http, DeliveryExecutionResult.Success());

            Assert.Throws<InvalidOperationException>(() =>
                new DeliveryExecutorDispatcher(new IDeliveryExecutor[] { a, b }));
        }

        private sealed class StubExecutor : IDeliveryExecutor
        {
            private readonly DeliveryExecutionResult _result;
            public StubExecutor(DestinationType type, DeliveryExecutionResult result)
            {
                DestinationType = type;
                _result = result;
            }
            public DestinationType DestinationType { get; }
            public int Calls { get; private set; }
            public Task<DeliveryExecutionResult> ExecuteAsync(
                DeliveryExecutionRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_result);
            }
        }
    }
}
