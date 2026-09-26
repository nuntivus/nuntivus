using NuntivusServer.Common;

namespace NuntivusServer.Api.Controllers.Sources
{
    public interface IWebhookIngestRequestValidator
    {
        Result Validate(WebhookIngestRequest request);
    }
}
