using System;
using System.Text;

namespace KairoServer.Infastructure.Messaging.Nats
{
    public sealed class KairoDurableConsumerNamer : IDurableConsumerNamer
    {
        private const string Prefix = "kairo";

        public string GetDurableName(string workerName, string? configuredDurableName)
        {
            var source = string.IsNullOrWhiteSpace(configuredDurableName)
                ? workerName
                : configuredDurableName!;

            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("WorkerName or DurableName must be provided.", nameof(workerName));

            var sanitized = Sanitize(source);
            if (sanitized.StartsWith(Prefix + "-", StringComparison.Ordinal))
                return sanitized;

            return $"{Prefix}-{sanitized}";
        }

        private static string Sanitize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    sb.Append(ch);
                }
                else if (ch is '-' or '_' or '.')
                {
                    sb.Append('-');
                }
            }

            var normalized = sb.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(normalized) ? Prefix : normalized;
        }
    }
}