using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Alerts
{
    public sealed class AlertWebhookSender(ISecretResolver secrets, IEndpointSecurityPolicy policy) : IAlertNotificationSender
    {
        public async Task<Result> SendAsync(AlertWebhookMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var endpoint = new Uri(message.Endpoint);
                var context = new EndpointSecurityContext(EndpointDirection.Destination, "https", "Alerts");
                var options = new EndpointSecurityOption { AllowHttpScheme = false };
                var validation = policy.ValidateEndpointUri(endpoint, context, options);
                if (validation.IsFailure) return validation;
                using var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false, UseProxy = false,
                    ConnectCallback = async (connection, token) =>
                    {
                        var addresses = await Dns.GetHostAddressesAsync(connection.DnsEndPoint.Host, token);
                        policy.ValidateResolvedAddresses(addresses, context, options).EnsureSuccess();
                        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try { await socket.ConnectAsync(addresses[0], connection.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
                        catch { socket.Dispose(); throw; }
                    }
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(message.Payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Nuntivus-Notification-Id", message.NotificationId.ToString("D"));
                if (message.SecretRef is not null)
                {
                    var secret = await secrets.ResolveAsync(message.SecretRef, cancellationToken);
                    if (secret.IsFailure) return Result.Failure(Error.Validation("Alert webhook credential is unavailable."));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Value);
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode ? Result.Success() : Result.Failure(Error.Validation("Alert webhook did not return success."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Result.Failure(Error.Validation("Alert webhook could not be delivered.")); }
        }
    }
}
