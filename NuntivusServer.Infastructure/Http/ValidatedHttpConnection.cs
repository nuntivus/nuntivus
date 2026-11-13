using System.Net;
using System.Net.Sockets;
using NuntivusServer.Application.Security;

namespace NuntivusServer.Infastructure.Http
{
    public interface IValidatedHttpConnection
    {
        ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken);
    }

    public sealed class UnsafeHttpEndpointException : IOException
    {
        public UnsafeHttpEndpointException() : base("HTTP endpoint is blocked by network policy.") { }
    }

    public sealed class ValidatedHttpConnection : IValidatedHttpConnection
    {
        private readonly IEndpointSecurityPolicy _policy;
        private readonly EndpointSecurityContext _context;
        private readonly EndpointSecurityOption _options;
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
        private readonly Func<IPAddress, int, CancellationToken, ValueTask<Stream>> _connect;

        public ValidatedHttpConnection(IEndpointSecurityPolicy policy, EndpointSecurityContext context,
            EndpointSecurityOption options,
            Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
            Func<IPAddress, int, CancellationToken, ValueTask<Stream>>? connect = null)
        {
            _policy = policy;
            _context = context;
            _options = options;
            _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
            _connect = connect ?? ConnectSocketAsync;
        }

        public SocketsHttpHandler CreateHandler() => new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = async (connection, token) =>
            {
                var uri = connection.InitialRequestMessage.RequestUri;
                if (uri is null || _policy.ValidateEndpointUri(uri, _context, _options).IsFailure)
                    throw new UnsafeHttpEndpointException();
                return await ConnectAsync(connection.DnsEndPoint, token);
            }
        };

        public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
        {
            var addresses = IPAddress.TryParse(endpoint.Host, out var address)
                ? new[] { address } : await _resolve(endpoint.Host, cancellationToken);
            if (addresses.Length == 0)
                throw new SocketException((int)SocketError.HostNotFound);
            if (_policy.ValidateResolvedAddresses(addresses, _context, _options).IsFailure)
                throw new UnsafeHttpEndpointException();

            SocketException? lastError = null;
            foreach (var candidate in addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { return await _connect(candidate, endpoint.Port, cancellationToken); }
                catch (SocketException error) { lastError = error; }
            }
            throw lastError!;
        }

        private static async ValueTask<Stream> ConnectSocketAsync(IPAddress address, int port, CancellationToken token)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, port, token);
                // SocketsHttpHandler still owns TLS and validates the original hostname.
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }

        public static bool IsPolicyFailure(Exception exception)
        {
            for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is UnsafeHttpEndpointException) return true;
            return false;
        }
    }
}
