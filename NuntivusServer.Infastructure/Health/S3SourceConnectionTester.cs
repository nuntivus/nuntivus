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

        public S3SourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.S3;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var credentials = await ConnectionProbeCredentials.SourceAwsAsync(config, _secrets, token);
                if (credentials.IsFailure) return Result<string>.Fail(credentials.Errors);
                var region = RegionEndpoint.GetBySystemName(config.GetProperty("region").GetString()!);
                using var client = credentials.Value is null
                    ? new AmazonS3Client(region) : new AmazonS3Client(credentials.Value, region);
                await client.GetBucketLocationAsync(new GetBucketLocationRequest
                {
                    BucketName = config.GetProperty("bucket").GetString()!
                }, token);
                return Result.Success("S3 bucket is accessible with the configured credentials.");
            }, cancellationToken);
    }
}
