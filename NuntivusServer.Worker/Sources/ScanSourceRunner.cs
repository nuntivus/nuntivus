using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Security;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Worker.Sources
{
    public sealed class ScanSourceRunner : ISourceRunner
    {
        private readonly ISourceEventProcessor _processor;
        private readonly ISourceIngestionStateRepository _state;
        private readonly ISecretResolver _secrets;
        private readonly string? _folderRoot;
        private readonly NuntivusServer.Infastructure.Sources.ISourceTransportClientFactory _clients;

        public ScanSourceRunner(SourceType type, ISourceEventProcessor processor,
            ISourceIngestionStateRepository state, ISecretResolver secrets,
            IConfiguration configuration, NuntivusServer.Infastructure.Sources.ISourceTransportClientFactory? clients = null)
        {
            Type = type;
            _processor = processor;
            _state = state;
            _secrets = secrets;
            _folderRoot = configuration["Sources:FolderRoot"];
            _clients = clients ?? new NuntivusServer.Infastructure.Sources.SourceTransportClientFactory();
        }

        public SourceType Type { get; }

        public async Task RunAsync(Source source, SourceLease lease, CancellationToken token)
        {
            using var document = JsonDocument.Parse(source.Configuration);
            var config = document.RootElement;
            if (Type == SourceType.FOLDER && string.IsNullOrWhiteSpace(_folderRoot))
                throw new InvalidOperationException("Sources:FolderRoot must be configured for folder Sources.");
            var interval = config.TryGetProperty("intervalSeconds", out var value) ? value.GetInt32() : 30;
            while (!token.IsCancellationRequested)
            {
                switch (Type)
                {
                    case SourceType.HTTP_POLLING: await PollHttpAsync(config, lease, token); break;
                    case SourceType.FOLDER: await ScanFolderAsync(config, lease, token); break;
                    case SourceType.S3: await ScanS3Async(config, lease, token); break;
                }
                await Task.Delay(TimeSpan.FromSeconds(interval), token);
            }
        }

        private async Task PollHttpAsync(JsonElement config, SourceLease lease, CancellationToken token)
        {
            var endpoint = new Uri(config.GetProperty("url").GetString()!);
            using var client = _clients.CreatePollingHttpClient(endpoint);
            var method = config.TryGetProperty("method", out var selectedMethod)
                ? selectedMethod.GetString()!.ToUpperInvariant() : "GET";
            if (method is not ("GET" or "POST"))
                throw new InvalidOperationException("HTTP polling method must be GET or POST.");
            using var request = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, endpoint);
            if (method == "POST" && config.TryGetProperty("body", out var requestBody))
                request.Content = new StringContent(requestBody.GetRawText(), Encoding.UTF8, "application/json");
            if (config.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                    request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
            }
            if (config.TryGetProperty("secretRef", out var reference))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " +
                    (await _secrets.ResolveAsync(reference.GetString()!, token)).EnsureValue());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 1024 * 1024)
                throw new InvalidOperationException("Polling response exceeds 1 MiB.");
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + read > 1024 * 1024) throw new InvalidOperationException("Polling response exceeds 1 MiB.");
                buffer.Write(bytes, 0, read);
            }
            var body = Encoding.UTF8.GetString(buffer.ToArray());
            var key = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
            if ((await _state.HasProcessedAsync(lease.SourceId, key, token)).EnsureValue()) return;
            (await _processor.ProcessAsync(Type, lease, body, key, token)).EnsureSuccess();
        }

        private async Task ScanFolderAsync(JsonElement config, SourceLease lease, CancellationToken token)
        {
            var path = Path.GetFullPath(config.GetProperty("path").GetString()!);
            if (string.IsNullOrWhiteSpace(_folderRoot)) throw new InvalidOperationException("Sources:FolderRoot must be configured.");
            var root = Path.GetFullPath(_folderRoot);
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Source folder must be inside the configured root.");
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            if (new DirectoryInfo(path).LinkTarget is not null) throw new InvalidOperationException("Linked source folders are not allowed.");
            for (var ancestor = new DirectoryInfo(path).Parent; ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor.LinkTarget is not null) throw new InvalidOperationException("Linked ancestor folders are not allowed.");
            foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                if (info.LinkTarget is not null || info.Length > 1024 * 1024) continue;
                var key = $"{info.Name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                if ((await _state.HasProcessedAsync(lease.SourceId, key, token)).EnsureValue()) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    8192, FileOptions.Asynchronous);
                if (new FileInfo(file).LinkTarget is not null) throw new InvalidOperationException("Linked source files are not allowed.");
                var body = await ReadBoundedAsync(stream, token);
                var after = new FileInfo(file);
                if (after.Length != info.Length || after.LastWriteTimeUtc != info.LastWriteTimeUtc)
                    throw new InvalidOperationException("Source file changed while reading.");
                (await _processor.ProcessAsync(Type, lease, body, key, token)).EnsureSuccess();
            }
        }

        private async Task ScanS3Async(JsonElement config, SourceLease lease, CancellationToken token)
        {
            var secret = config.TryGetProperty("secretRef", out var reference)
                ? (await _secrets.ResolveAsync(reference.GetString()!, token)).EnsureValue() : null;
            using var client = _clients.CreateS3(config,
                secret is not null && config.TryGetProperty("accessKeyId", out var accessKey)
                    ? new BasicAWSCredentials(accessKey.GetString(), secret) : null);
            var bucket = config.GetProperty("bucket").GetString()!;
            var prefix = config.GetProperty("prefix").GetString()!;
            string? nextToken = null;
            do
            {
                var listed = await client.ListObjectsV2Async(new ListObjectsV2Request
                    { BucketName = bucket, Prefix = prefix, MaxKeys = 100, ContinuationToken = nextToken }, token);
                foreach (var item in listed.S3Objects)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.Size > 1024 * 1024 || !item.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    var key = $"{item.Key}:{item.ETag}";
                    if ((await _state.HasProcessedAsync(lease.SourceId, key, token)).EnsureValue()) continue;
                    using var response = await client.GetObjectAsync(bucket, item.Key, token);
                    if (response.ETag != item.ETag) throw new InvalidOperationException("S3 object changed while reading.");
                    var body = await ReadBoundedAsync(response.ResponseStream, token);
                    (await _processor.ProcessAsync(Type, lease, body, key, token)).EnsureSuccess();
                }
                nextToken = listed.IsTruncated ? listed.NextContinuationToken : null;
            } while (nextToken is not null);
        }

        private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + read > 1024 * 1024) throw new InvalidOperationException("Source item exceeds 1 MiB.");
                output.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
