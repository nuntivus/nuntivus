using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpApiSourceConnectionTester : ISourceConnectionTester
    {
        public SourceType Type => SourceType.HTTP_API;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success("Inbound HTTP source configuration is valid. No outbound connection is required; this does not verify the public ingestion URL."));
    }
}
