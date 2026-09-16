using System;

namespace KairoServer.Application.Deliveries.Execution
{
    public sealed class DeliveryOrderingOption
    {
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    }
}
