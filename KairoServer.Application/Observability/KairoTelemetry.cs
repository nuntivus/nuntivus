using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KairoServer.Application.Observability
{
    public static class KairoTelemetry
    {
        public const string ActivitySourceName = "KairoServer";
        public const string MeterName = "KairoServer";

        public const string EventIdTag = "kairo.event.id";
        public const string DeliveryIdTag = "kairo.delivery.id";
        public const string ContractIdTag = "kairo.contract.id";
        public const string CustomerIdTag = "kairo.customer.id";
        public const string OutcomeTag = "kairo.outcome";
        public const string WorkerTag = "kairo.worker";

        public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
        public static readonly Meter Meter = new(MeterName);
    }
}
