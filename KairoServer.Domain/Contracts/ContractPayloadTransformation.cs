using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace KairoServer.Domain.Contracts
{
    public sealed class ContractPayloadTransformation
    {
        private readonly IReadOnlyList<ContractPayloadFieldMapping> _fieldMappings;

        public int Version { get; }
        public IReadOnlyList<ContractPayloadFieldMapping> FieldMappings => _fieldMappings;

        [JsonConstructor]
        public ContractPayloadTransformation(
            int Version,
            IReadOnlyList<ContractPayloadFieldMapping> FieldMappings)
        {
            if (Version <= 0)
                throw new ArgumentOutOfRangeException(nameof(Version), "Version must be greater than zero.");

            if (FieldMappings is null)
                throw new ArgumentNullException(nameof(FieldMappings));

            this.Version = Version;
            _fieldMappings = FieldMappings
                .Select(m => new ContractPayloadFieldMapping(m.SourceSelector, m.TargetSelector))
                .ToArray();
        }
    }
}
