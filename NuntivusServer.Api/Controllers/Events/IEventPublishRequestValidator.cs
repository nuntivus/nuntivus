using NuntivusServer.Common;

namespace NuntivusServer.Api.Controllers.Events
{
    public interface IEventPublishRequestValidator
    {
        Result Validate(PublishEventRequest request);
    }
}
