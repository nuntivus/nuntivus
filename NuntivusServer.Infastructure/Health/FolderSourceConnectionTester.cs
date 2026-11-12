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
            => ConnectionProbe.RunAsync(token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var path = document.RootElement.GetProperty("path").GetString()!;
                token.ThrowIfCancellationRequested();
                using var files = Directory.EnumerateFiles(path, "*.json").GetEnumerator();
                files.MoveNext();
                return Task.FromResult(Result.Success("Folder JSON listing is accessible on the API host. No files read, moved or deleted; Worker access is not verified."));
            }, cancellationToken);
    }
}
