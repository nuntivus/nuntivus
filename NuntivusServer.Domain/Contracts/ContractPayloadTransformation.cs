using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NuntivusServer.Domain.Contracts
{
    public sealed class ContractPayloadTransformation
    {
        private readonly IReadOnlyList<ContractPayloadFieldMapping> _fieldMappings;

        public int Version { get; }
        public IReadOnlyList<ContractPayloadFieldMapping> FieldMappings => _fieldMappings;
        public JsonElement? Expression { get; }
        public string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Canonicalize(JsonSerializer.SerializeToNode(new { Version, FieldMappings, Expression }))!.ToJsonString()))).ToLowerInvariant();

        private static JsonNode? Canonicalize(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(property => property.Key, StringComparer.Ordinal)
                .Select(property => new KeyValuePair<string, JsonNode?>(property.Key, Canonicalize(property.Value)))),
            JsonArray array => new JsonArray(array.Select(Canonicalize).ToArray()),
            _ => node?.DeepClone()
        };

        [JsonConstructor]
        public ContractPayloadTransformation(
            int Version,
            IReadOnlyList<ContractPayloadFieldMapping> FieldMappings,
            JsonElement? Expression = null)
        {
            if (Version <= 0)
                throw new ArgumentOutOfRangeException(nameof(Version), "Version must be greater than zero.");

            if (FieldMappings is null)
                throw new ArgumentNullException(nameof(FieldMappings));

            this.Version = Version;
            this.Expression = Expression?.Clone();
            _fieldMappings = FieldMappings
                .Select(m => new ContractPayloadFieldMapping(m.SourceSelector, m.TargetSelector))
                .ToArray();
        }
    }
}
