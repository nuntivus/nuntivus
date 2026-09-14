using System;
using System.Collections.Generic;
using System.Linq;

namespace KairoServer.Domain.Contracts
{
    public sealed class ContractFilterRule
    {
        private readonly IReadOnlyList<string> _values;

        public string Selector { get; }
        public ContractFilterOperator Operator { get; }
        public string? Value { get; }
        public IReadOnlyList<string> Values => _values;

        public ContractFilterRule(
            string selector,
            ContractFilterOperator @operator,
            string? value,
            IEnumerable<string>? values)
        {
            if (string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("Selector is required.", nameof(selector));

            Selector = selector.Trim();
            Operator = @operator;
            Value = value;
            _values = values?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToArray()
                ?? Array.Empty<string>();
        }
    }
}