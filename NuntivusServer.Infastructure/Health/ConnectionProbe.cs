using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using Amazon.Runtime;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Health
{
    internal static class ConnectionProbe
    {
        internal static async Task<Result<string>> RunAsync(Func<CancellationToken, Task<Result<string>>> probe,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                return await probe(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result.Failure<string>(Error.Validation("Connection test timed out."));
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or AmazonServiceException or ArgumentException or InvalidOperationException)
            {
                return Result.Failure<string>(Error.Validation($"Connection test failed: {ex.Message}"));
            }
        }

        internal static async Task<Result<string>> TestTlsAsync(Uri uri, int defaultPort, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, uri.IsDefaultPort ? defaultPort : uri.Port, cancellationToken);
            using var stream = new SslStream(client.GetStream(), false);
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host }, cancellationToken);
            return Result.Success("TLS endpoint reachable (broker credentials and permissions are not verified).");
        }

        internal static async Task<Result<string>> TestTcpAsync(Uri uri, int defaultPort, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, uri.IsDefaultPort ? defaultPort : uri.Port, cancellationToken);
            return Result.Success("Kafka TCP endpoint reachable (broker credentials and permissions are not verified).");
        }

        internal static Result<Uri> KafkaEndpoint(string servers)
        {
            var first = servers.Split(',')[0].Trim();
            return Uri.TryCreate($"tls://{first}", UriKind.Absolute, out var uri)
                ? Result.Success(uri)
                : Result.Failure<Uri>(Error.Validation("Invalid Kafka bootstrap server."));
        }

        internal static async Task<Result<string>> TestHttpAsync(IHttpClientFactory factory, string url,
            string headRejectedMessage, string successMessage, string failureSuffix, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await factory.CreateClient("connection-test").SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode == HttpStatusCode.MethodNotAllowed
                ? Result.Success(headRejectedMessage)
                : response.IsSuccessStatusCode
                    ? Result.Success(successMessage)
                    : Result.Failure<string>(Error.Validation($"HTTP endpoint returned {(int)response.StatusCode} to HEAD{failureSuffix}"));
        }
    }
}
