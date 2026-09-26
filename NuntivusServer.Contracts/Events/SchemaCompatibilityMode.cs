using System.Text.Json.Serialization;

namespace NuntivusServer.Contracts.Events
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SchemaCompatibilityMode
    {
        Backward = 1,
        Forward = 2
    }
}