using System;

namespace KairoServer.Infastructure.Messaging.Nats
{
    /// <summary>
    /// Configuration options for connecting to a NATS server with JetStream enabled.
    /// </summary>
    public sealed class NatsOption
    {
        /// <summary>
        /// Comma-separated list of NATS server URLs (e.g. "nats://localhost:4222").
        /// </summary>
        public string Url { get; set; } = "nats://localhost:4222";

        /// <summary>
        /// Optional client connection name (useful for monitoring).
        /// </summary>
        public string? Name { get; set; } = "kairo-server";

        /// <summary>
        /// Optional authentication token.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// Optional username for user/password authentication.
        /// </summary>
        public string? User { get; set; }

        /// <summary>
        /// Optional password for user/password authentication.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Optional path to a NATS credentials (.creds) file for JWT-based auth.
        /// </summary>
        public string? CredsFile { get; set; }

        /// <summary>
        /// Timeout for publishing and awaiting JetStream ACKs.
        /// </summary>
        public TimeSpan PublishAckTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Timeout for establishing a connection.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// JetStream stream name that must accept the published subjects.
        /// </summary>
        public string Stream { get; set; } = "KAIRO_EVENTS";

        /// <summary>
        /// Subject filters the stream captures. Wildcards are supported (e.g. "event.&gt;").
        /// </summary>
        public string[] Subjects { get; set; } = new[] { "event.>", "delivery.>" };

        /// <summary>
        /// Duplicate detection window used by JetStream for Nats-Msg-Id deduplication.
        /// </summary>
        public TimeSpan DuplicateWindow { get; set; } = TimeSpan.FromMinutes(2);
    }
}
