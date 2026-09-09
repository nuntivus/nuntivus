using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;
using DtoDestinationType = KairoServer.Contracts.CustomerContracts.DestinationType;

namespace KairoServer.Application.Contracts
{
    internal static class ContractMapping
    {
        public static ContractView ToView(this Contract c) =>
            new(
                c.Id,
                c.CustomerId,
                c.Enabled,
                new System.Collections.Generic.List<string>(c.EventTypes),
                MapDestinationType(c.Destination.Type),
                c.Destination.Configuration,
                c.CreatedAt,
                c.UpdatedAt);

        public static ContractCreated ToCreated(this Contract c) =>
            new(
                c.Id,
                c.CustomerId,
                c.Enabled,
                new System.Collections.Generic.List<string>(c.EventTypes),
                MapDestinationType(c.Destination.Type),
                c.CreatedAt);

        public static DtoDestinationType MapDestinationType(DomainDestinationType t) => t switch
        {
            DomainDestinationType.Http => DtoDestinationType.Http,
            _ => (DtoDestinationType)(int)t
        };
    }
}
