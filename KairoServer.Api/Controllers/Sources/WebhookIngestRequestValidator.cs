using System.Text;
using KairoServer.Api.Common;
using KairoServer.Common;

namespace KairoServer.Api.Controllers.Sources
{
    public sealed class WebhookIngestRequestValidator : IWebhookIngestRequestValidator
    {
        private readonly ApiSecurityOption _security;

        public WebhookIngestRequestValidator(ApiSecurityOption security)
        {
            _security = security;
        }

        public Result Validate(WebhookIngestRequest request)
        {
            var payloadBytes = Encoding.UTF8.GetByteCount(request.Payload.GetRawText());
            if (payloadBytes > _security.MaxEventPayloadBytes)
            {
                return Result.Failure(Error.Validation(
                    $"payload exceeds maximum allowed size of {_security.MaxEventPayloadBytes} bytes."));
            }

            var metadataBytes = request.Metadata.HasValue
                ? Encoding.UTF8.GetByteCount(request.Metadata.Value.GetRawText())
                : 0;
            if (metadataBytes > _security.MaxEventMetadataBytes)
            {
                return Result.Failure(Error.Validation(
                    $"metadata exceeds maximum allowed size of {_security.MaxEventMetadataBytes} bytes."));
            }

            return Result.Success();
        }
    }
}
