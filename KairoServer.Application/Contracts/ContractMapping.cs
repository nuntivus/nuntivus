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
                ToSchemaTargetViews(c.SchemaTargets),
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
                ToSchemaTargetViews(c.SchemaTargets),
                MapDestinationType(c.Destination.Type),
                c.CreatedAt);

        public static System.Collections.Generic.IReadOnlyCollection<ContractSchemaTargetView> ToSchemaTargetViews(
            System.Collections.Generic.IEnumerable<ContractSchemaTarget> targets)
        {
            var list = new System.Collections.Generic.List<ContractSchemaTargetView>();
            foreach (var target in targets)
            {
                list.Add(new ContractSchemaTargetView(target.EventType, target.Version));
            }

            return list;
        }

        public static DtoDestinationType MapDestinationType(DomainDestinationType t) => t switch
        {
            DomainDestinationType.Http => DtoDestinationType.Http,
            _ => (DtoDestinationType)(int)t
        };
    }
}
