using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using System.Collections.Generic;
using System.Linq;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;
using DomainFilterOperator = NuntivusServer.Domain.Contracts.ContractFilterOperator;
using DtoDestinationType = NuntivusServer.Contracts.CustomerContracts.DestinationType;
using DtoFilterOperator = NuntivusServer.Contracts.CustomerContracts.ContractFilterOperator;

namespace NuntivusServer.Application.Contracts
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
                ToTransformationView(c.Transformation),
                ToRateLimitConfig(c.RateLimit),
                ToOrderingConfig(c.Ordering),
                MapDestinationType(c.Destination.Type),
                c.Destination.Configuration,
                c.CreatedAt,
                c.UpdatedAt,
                c.Name,
                new System.Collections.Generic.List<System.Guid>(c.SourceIds));

        public static ContractCreated ToCreated(this Contract c) =>
            new(
                c.Id,
                c.CustomerId,
                c.Enabled,
                new System.Collections.Generic.List<string>(c.EventTypes),
                ToSchemaTargetViews(c.SchemaTargets),
                ToFilterView(c.Filter),
                ToTransformationView(c.Transformation),
                ToRateLimitConfig(c.RateLimit),
                ToOrderingConfig(c.Ordering),
                MapDestinationType(c.Destination.Type),
                c.CreatedAt,
                c.Name,
                new System.Collections.Generic.List<System.Guid>(c.SourceIds),
                c.Destinations.Where(binding => binding.DestinationId.HasValue)
                    .Select(binding => binding.DestinationId!.Value).ToArray());

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

        public static ContractPayloadTransformation? ToDomain(this ContractPayloadTransformationRequest? transformation)
        {
            if (transformation is null)
                return null;

            var mappings = (transformation.FieldMappings ?? System.Array.Empty<ContractPayloadFieldMappingRequest>())
                .Select(m => new ContractPayloadFieldMapping(m.SourceSelector, m.TargetSelector))
                .ToArray();

            if (mappings.Length == 0 && transformation.Expression is null)
                return null;

            return new ContractPayloadTransformation(transformation.Version, mappings, transformation.Expression);
        }

        public static ContractRateLimit? ToDomain(this ContractRateLimitConfig? rateLimit) =>
            rateLimit is null
                ? null
                : ContractRateLimit.Create(rateLimit.PerSecond, rateLimit.PerMinute, rateLimit.Burst);

        public static ContractRateLimitConfig? ToRateLimitConfig(ContractRateLimit? rateLimit) =>
            rateLimit is null
                ? null
                : new ContractRateLimitConfig(rateLimit.PerSecond, rateLimit.PerMinute, rateLimit.Burst);

        public static ContractOrdering? ToDomain(this ContractOrderingConfig? ordering) =>
            ordering is null
                ? null
                : ContractOrdering.Create(ordering.KeySelector);

        public static ContractOrderingConfig? ToOrderingConfig(ContractOrdering? ordering) =>
            ordering is null
                ? null
                : new ContractOrderingConfig(ordering.KeySelector);

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
            DomainDestinationType.Kafka => DtoDestinationType.Kafka,
            DomainDestinationType.Nats => DtoDestinationType.Nats,
            DomainDestinationType.RabbitMq => DtoDestinationType.RabbitMq,
            DomainDestinationType.Sqs => DtoDestinationType.Sqs,
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

        public static ContractPayloadTransformationView? ToTransformationView(ContractPayloadTransformation? transformation)
        {
            if (transformation is null || (transformation.FieldMappings.Count == 0 && transformation.Expression is null))
                return null;

            var mappings = transformation.FieldMappings
                .Select(m => new ContractPayloadFieldMappingView(m.SourceSelector, m.TargetSelector))
                .ToArray();

            return new ContractPayloadTransformationView(transformation.Version, mappings, transformation.Expression, transformation.Identity);
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
