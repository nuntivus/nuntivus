using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using System.Collections.Generic;
using System.Linq;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;
using DomainFilterOperator = KairoServer.Domain.Contracts.ContractFilterOperator;
using DtoDestinationType = KairoServer.Contracts.CustomerContracts.DestinationType;
using DtoFilterOperator = KairoServer.Contracts.CustomerContracts.ContractFilterOperator;

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
                ToFilterView(c.Filter),
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
                ToFilterView(c.Filter),
                MapDestinationType(c.Destination.Type),
                c.CreatedAt);

        public static ContractFilter? ToDomain(this ContractFilterRequest? filter)
        {
            if (filter is null)
                return null;

            var metadata = (filter.MetadataRules ?? Array.Empty<ContractFilterRuleRequest>())
                .Select(ToDomain)
                .ToArray();

            var payload = (filter.PayloadRules ?? Array.Empty<ContractFilterRuleRequest>())
                .Select(ToDomain)
                .ToArray();

            if (metadata.Length == 0 && payload.Length == 0)
                return null;

            return new ContractFilter(metadata, payload);
        }

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

        public static ContractFilterView? ToFilterView(ContractFilter? filter)
        {
            if (filter is null || !filter.HasRules)
                return null;

            var metadata = filter.MetadataRules.Select(ToFilterRuleView).ToArray();
            var payload = filter.PayloadRules.Select(ToFilterRuleView).ToArray();

            return new ContractFilterView(metadata, payload);
        }

        public static DomainFilterOperator MapFilterOperator(DtoFilterOperator value) => value switch
        {
            DtoFilterOperator.Equals => DomainFilterOperator.Equals,
            DtoFilterOperator.NotEquals => DomainFilterOperator.NotEquals,
            DtoFilterOperator.GreaterThan => DomainFilterOperator.GreaterThan,
            DtoFilterOperator.GreaterThanOrEqual => DomainFilterOperator.GreaterThanOrEqual,
            DtoFilterOperator.LessThan => DomainFilterOperator.LessThan,
            DtoFilterOperator.LessThanOrEqual => DomainFilterOperator.LessThanOrEqual,
            DtoFilterOperator.Contains => DomainFilterOperator.Contains,
            DtoFilterOperator.StartsWith => DomainFilterOperator.StartsWith,
            DtoFilterOperator.EndsWith => DomainFilterOperator.EndsWith,
            DtoFilterOperator.In => DomainFilterOperator.In,
            DtoFilterOperator.NotIn => DomainFilterOperator.NotIn,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported contract filter operator.")
        };

        public static DtoFilterOperator MapFilterOperator(DomainFilterOperator value) => value switch
        {
            DomainFilterOperator.Equals => DtoFilterOperator.Equals,
            DomainFilterOperator.NotEquals => DtoFilterOperator.NotEquals,
            DomainFilterOperator.GreaterThan => DtoFilterOperator.GreaterThan,
            DomainFilterOperator.GreaterThanOrEqual => DtoFilterOperator.GreaterThanOrEqual,
            DomainFilterOperator.LessThan => DtoFilterOperator.LessThan,
            DomainFilterOperator.LessThanOrEqual => DtoFilterOperator.LessThanOrEqual,
            DomainFilterOperator.Contains => DtoFilterOperator.Contains,
            DomainFilterOperator.StartsWith => DtoFilterOperator.StartsWith,
            DomainFilterOperator.EndsWith => DtoFilterOperator.EndsWith,
            DomainFilterOperator.In => DtoFilterOperator.In,
            DomainFilterOperator.NotIn => DtoFilterOperator.NotIn,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported contract filter operator.")
        };

        private static ContractFilterRule ToDomain(ContractFilterRuleRequest rule) =>
            new(rule.Selector, MapFilterOperator(rule.Operator), rule.Value, rule.Values ?? Array.Empty<string>());

        private static ContractFilterRuleView ToFilterRuleView(ContractFilterRule rule) =>
            new(
                rule.Selector,
                MapFilterOperator(rule.Operator),
                rule.Value,
                new List<string>(rule.Values));
    }
}
