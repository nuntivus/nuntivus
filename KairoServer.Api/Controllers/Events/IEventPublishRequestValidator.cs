using KairoServer.Common;

namespace KairoServer.Api.Controllers.Events
{
    public interface IEventPublishRequestValidator
    {
        Result Validate(PublishEventRequest request);
    }
}
