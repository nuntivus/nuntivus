using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class InboundSourceAdapter : ISourceAdapter
    {
        public InboundSourceAdapter(SourceType type) => Type = type;
        public SourceType Type { get; }
        public EndpointSecurityContext SecurityContext => new(EndpointDirection.Source, Type.ToString(), "events.publish");

        public Task<Result<CanonicalSourceEvent>> NormalizeAsync(object adapterContext, CancellationToken cancellationToken) =>
            Task.FromResult(adapterContext is InboundSourceEventContext context
                ? InboundSourceEnvelope.Parse(context)
                : Result.Failure<CanonicalSourceEvent>(Error.Validation("Inbound Source adapter context is invalid.")));
    }
}
