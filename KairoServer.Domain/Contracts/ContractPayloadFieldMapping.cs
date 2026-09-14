using System;
using System.Text.Json.Serialization;

namespace KairoServer.Domain.Contracts
{
    public sealed class ContractPayloadFieldMapping
    {
        public string SourceSelector { get; }
        public string TargetSelector { get; }

        [JsonConstructor]
        public ContractPayloadFieldMapping(string SourceSelector, string TargetSelector)
        {
            if (string.IsNullOrWhiteSpace(SourceSelector))
                throw new ArgumentException("Source selector is required.", nameof(SourceSelector));

            if (string.IsNullOrWhiteSpace(TargetSelector))
                throw new ArgumentException("Target selector is required.", nameof(TargetSelector));

            this.SourceSelector = SourceSelector.Trim();
            this.TargetSelector = TargetSelector.Trim();
        }
    }
}
