using KairoServer.Application.Messaging;
using KairoServer.Common;
using KairoServer.Common.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Messaging.Nats
{
    /// <summary>
    /// Publishes messages to a NATS JetStream stream, awaiting the server ACK
    /// to guarantee at-least-once delivery semantics required by the outbox pattern.
    /// The outbox message id is forwarded as the <c>Nats-Msg-Id</c> header so
    /// JetStream can deduplicate retries within the stream's duplicate window.
    /// </summary>
    public sealed class NatsEventBusPublisher : IEventBusPublisher
    {

        private readonly INatsJSContext _jetStream;
        private readonly NatsOption _options;
        private readonly IAppLogger<NatsEventBusPublisher> _logger;

        public NatsEventBusPublisher(
            INatsJSContext jetStream,
            NatsOption options,
            IAppLogger<NatsEventBusPublisher> logger)
        {
            _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result> PublishAsync(
            string subject,
            string payload,
            string messageId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Result.Failure("nats.subject_required", "Subject must be provided.");
            }

            if (string.IsNullOrWhiteSpace(messageId))
            {
                return Result.Failure("nats.message_id_required", "Message id must be provided for deduplication.");
            }

            if (payload is null)
            {
                return Result.Failure("nats.payload_required", "Payload must be provided.");
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_options.PublishAckTimeout);

                var data = Encoding.UTF8.GetBytes(payload);

                _logger.Debug(
                    "Publishing to NATS subject {Subject} (msgId {MessageId}, {Bytes} bytes).",
                    subject,
                    messageId,
                    data.Length);

                var ack = await _jetStream.PublishAsync(
                    subject: subject,
                    data: data,
                    opts: new NatsJSPubOpts { 
                        MsgId = messageId
                    },
                    cancellationToken: cts.Token);

                ack.EnsureSuccess();

                _logger.Information(
                    "Published to NATS subject {Subject} (msgId {MessageId}, stream {Stream}, seq {Sequence}, duplicate {Duplicate}).",
                    subject,
                    messageId,
                    ack.Stream,
                    ack.Seq,
                    ack.Duplicate);

                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to publish to NATS subject {Subject} (msgId {MessageId}).",
                    subject,
                    messageId);

                return Result.Failure(ex);
            }
        }
    }
}
