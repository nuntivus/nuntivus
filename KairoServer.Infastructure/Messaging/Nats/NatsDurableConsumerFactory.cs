using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace KairoServer.Infastructure.Messaging.Nats
{
    public sealed class NatsDurableConsumerFactory : IDurableConsumerFactory
    {
        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _natsOptions;
        private readonly IDurableConsumerNamer _namer;
        private readonly IAppLogger<NatsDurableConsumerFactory> _logger;

        public NatsDurableConsumerFactory(
            INatsJSContext jetStream,
            NatsOption natsOptions,
            IDurableConsumerNamer namer,
            IAppLogger<NatsDurableConsumerFactory> logger)
        {
            _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
            _natsOptions = natsOptions ?? throw new ArgumentNullException(nameof(natsOptions));
            _namer = namer ?? throw new ArgumentNullException(nameof(namer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DurableConsumerHandle> CreateOrUpdateAsync(
            DurableConsumerSpec spec,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(spec);

            var durableName = _namer.GetDurableName(spec.WorkerName, spec.DurableName);
            var config = new ConsumerConfig(durableName)
            {
                FilterSubject = spec.FilterSubject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                AckWait = spec.AckWait,
                MaxAckPending = spec.MaxAckPending,
                MaxDeliver = spec.MaxDeliver,
            };

            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _natsOptions.Stream,
                config,
                cancellationToken);

            _logger.Information(
                "JetStream durable consumer {Durable} ready on stream {Stream} (filter {Filter}, ackWait {AckWait}, maxDeliver {MaxDeliver}).",
                durableName,
                _natsOptions.Stream,
                spec.FilterSubject,
                spec.AckWait,
                spec.MaxDeliver);

            return new DurableConsumerHandle(durableName, consumer);
        }
    }
}