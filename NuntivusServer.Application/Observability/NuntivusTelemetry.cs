using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NuntivusServer.Application.Observability
{
    public static class NuntivusTelemetry
    {
        public const string ActivitySourceName = "NuntivusServer";
        public const string MeterName = "NuntivusServer";

        public const string EventIdTag = "nuntivus.event.id";
        public const string DeliveryIdTag = "nuntivus.delivery.id";
        public const string ContractIdTag = "nuntivus.contract.id";
        public const string CustomerIdTag = "nuntivus.customer.id";
        public const string OutcomeTag = "nuntivus.outcome";
        public const string WorkerTag = "nuntivus.worker";

        public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
        public static readonly Meter Meter = new(MeterName);
    }
}
