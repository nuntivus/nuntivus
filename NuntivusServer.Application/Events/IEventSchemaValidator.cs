using NuntivusServer.Common;

namespace NuntivusServer.Application.Events
{
    public interface IEventSchemaValidator
    {
        Result ValidateSchema(string schema);
        Result ValidatePayload(string schema, string payload);
    }
}
