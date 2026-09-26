using System.Text.Json.Serialization;

namespace NuntivusServer.Contracts.Events
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EventSchemaState
    {
        Draft = 1,
        Active = 2,
        Deprecated = 3
    }
}