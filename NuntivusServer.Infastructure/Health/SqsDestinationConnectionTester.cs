using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class SqsDestinationConnectionTester : IDestinationConnectionTester
    {
        private readonly NuntivusServer.Infastructure.Deliveries.Sqs.SqsDeliveryExecutor _executor;

        public SqsDestinationConnectionTester(NuntivusServer.Infastructure.Deliveries.Sqs.SqsDeliveryExecutor executor) => _executor = executor;

        public DestinationType Type => DestinationType.Sqs;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            TestAsync(destination, "{}", cancellationToken);

        public Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken) =>
            DestinationTestRequest.SendAsync(_executor, destination, payload, cancellationToken);
    }
}
