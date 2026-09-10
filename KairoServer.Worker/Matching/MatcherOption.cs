using System;

namespace KairoServer.Worker.Matching
{
    /// <summary>
    /// Configuration for the contract matcher JetStream consumer loop.
    /// </summary>
    public sealed class MatcherOption
    {
        /// <summary>
        /// Durable consumer name shared by all matcher worker replicas so
        /// JetStream distributes messages across them.
        /// </summary>
        public string DurableName { get; set; } = "contract-matcher";

        /// <summary>
        /// Subject filter for the durable consumer.
        /// </summary>
        public string FilterSubject { get; set; } = "event.accepted";

        /// <summary>
        /// Maximum messages per pull-fetch batch.
        /// </summary>
        public int BatchSize { get; set; } = 32;

        /// <summary>
        /// Maximum time a fetch waits for messages before returning empty.
        /// </summary>
        public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Backoff applied to <c>NAK</c> so failed messages are redelivered
        /// after a short delay instead of tight-looping.
        /// </summary>
        public TimeSpan NakDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// JetStream <c>MaxAckPending</c> cap for the durable consumer.
        /// </summary>
        public int MaxAckPending { get; set; } = 1000;
    }
}
