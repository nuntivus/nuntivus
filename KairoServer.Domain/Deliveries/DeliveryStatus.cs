namespace KairoServer.Domain.Deliveries
{
    /// <summary>
    /// Lifecycle status of a <see cref="Delivery"/>.
    /// </summary>
    public enum DeliveryStatus
    {
        Pending = 0,
        InFlight = 1,
        Delivered = 2,
        Failed = 3,
    }
}
