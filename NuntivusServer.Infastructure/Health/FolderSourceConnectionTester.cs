using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class FolderSourceConnectionTester : ISourceConnectionTester
    {
        public SourceType Type => SourceType.FOLDER;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            var path = document.RootElement.GetProperty("path").GetString()!;
            return Task.FromResult(Directory.Exists(path)
                ? Result.Success("Folder is accessible on the API host (Worker access is not verified).")
                : Result.Failure<string>(Error.Validation("Folder is not accessible on the API host.")));
        }
    }
}
