using KairoServer.Common;

namespace KairoServer.Api.Controllers.Sources
{
    public interface IWebhookIngestRequestValidator
    {
        Result Validate(WebhookIngestRequest request);
    }
}
