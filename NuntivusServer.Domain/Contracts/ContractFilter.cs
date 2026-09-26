using System;
using System.Collections.Generic;
using System.Linq;

namespace NuntivusServer.Domain.Contracts
{
    public sealed class ContractFilter
    {
        private readonly IReadOnlyList<ContractFilterRule> _metadataRules;
        private readonly IReadOnlyList<ContractFilterRule> _payloadRules;

        public IReadOnlyList<ContractFilterRule> MetadataRules => _metadataRules;
        public IReadOnlyList<ContractFilterRule> PayloadRules => _payloadRules;
        public bool HasRules => _metadataRules.Count > 0 || _payloadRules.Count > 0;

        public ContractFilter(
            IEnumerable<ContractFilterRule>? metadataRules,
            IEnumerable<ContractFilterRule>? payloadRules)
        {
            _metadataRules = Clone(metadataRules);
            _payloadRules = Clone(payloadRules);
        }

        private static IReadOnlyList<ContractFilterRule> Clone(IEnumerable<ContractFilterRule>? source)
        {
            if (source is null)
                return Array.Empty<ContractFilterRule>();

            return source
                .Select(r => new ContractFilterRule(r.Selector, r.Operator, r.Value, r.Values))
                .ToArray();
        }
    }
}