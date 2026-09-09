namespace KairoServer.Domain.Contracts
{
    /// <summary>
    /// Delivery mechanism used by a Contract to hand off matched events to a Customer.
    /// </summary>
    public enum DestinationType
    {
        Http = 1
    }
}
