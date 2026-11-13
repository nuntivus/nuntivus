using System.Text.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class S3SourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;
        private readonly Sources.ISourceTransportClientFactory _clients;

        public S3SourceConnectionTester(ISecretResolver secrets, Sources.ISourceTransportClientFactory? clients = null)
        {
            _secrets = secrets;
            _clients = clients ?? new Sources.SourceTransportClientFactory();
        }

        public SourceType Type => SourceType.S3;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var credentials = await ConnectionProbeCredentials.SourceAwsAsync(config, _secrets, token);
                if (credentials.IsFailure) return Result<string>.Fail(credentials.Errors);
                using var client = _clients.CreateS3(config, credentials.Value);
                await client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = config.GetProperty("bucket").GetString()!,
                    Prefix = config.GetProperty("prefix").GetString()!,
                    MaxKeys = 1
                }, token);
                return Result.Success("S3 bucket/prefix listing is accessible with the configured credentials. No objects read, deleted or moved; object-read permission is not verified.");
            }, cancellationToken);
    }
}
