using System.Net;
using System.Net.Sockets;
using NuntivusServer.Application.Security;
using NuntivusServer.Infastructure.Http;

namespace NuntivusServer.UnitTests.Security
{
    public sealed class ValidatedHttpConnectionTests
    {
        private static readonly EndpointSecurityContext Context =
            new(EndpointDirection.Destination, "http", "Destination");

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("10.0.0.1")]
        [InlineData("169.254.169.254")]
        [InlineData("172.16.0.1")]
        [InlineData("192.168.0.1")]
        [InlineData("100.64.0.1")]
        [InlineData("224.0.0.1")]
        [InlineData("::")]
        [InlineData("::1")]
        [InlineData("fc00::1")]
        [InlineData("fe80::1")]
        [InlineData("::ffff:127.0.0.1")]
        public async Task Unsafe_addresses_never_connect(string ip)
        {
            var connections = 0;
            var connection = Build(_ => Task.FromResult(new[] { IPAddress.Parse(ip) }),
                (_, _, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
            await Assert.ThrowsAsync<UnsafeHttpEndpointException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), default).AsTask());
            Assert.Equal(0, connections);
        }

        [Fact]
        public async Task Mixed_public_private_results_are_rejected_before_connecting()
        {
            var connections = 0;
            var connection = Build(_ => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Loopback }),
                (_, _, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
            await Assert.ThrowsAsync<UnsafeHttpEndpointException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), default).AsTask());
            Assert.Equal(0, connections);
        }

        [Fact]
        public async Task Validated_IP_is_connected_without_a_second_resolution()
        {
            var resolutions = 0;
            IPAddress? connected = null;
            var connection = Build(_ =>
            {
                resolutions++;
                return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
            }, (ip, port, _) =>
            {
                connected = ip;
                Assert.Equal(443, port);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            });
            using var stream = await connection.ConnectAsync(new DnsEndPoint("example.test", 443), default);
            Assert.Equal(1, resolutions);
            Assert.Equal(IPAddress.Parse("93.184.216.34"), connected);
        }

        [Fact]
        public async Task Rebinding_is_checked_on_each_new_connection()
        {
            var resolutions = 0;
            var connections = 0;
            var connection = Build(_ => Task.FromResult(new[] {
                ++resolutions == 1 ? IPAddress.Parse("93.184.216.34") : IPAddress.Loopback }),
                (_, _, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
            using var first = await connection.ConnectAsync(new DnsEndPoint("example.test", 443), default);
            await Assert.ThrowsAsync<UnsafeHttpEndpointException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), default).AsTask());
            Assert.Equal(1, connections);
        }

        [Fact]
        public async Task DNS_failure_is_not_followed_by_a_connection()
        {
            var connections = 0;
            var connection = Build(_ => throw new SocketException((int)SocketError.HostNotFound),
                (_, _, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
            await Assert.ThrowsAsync<SocketException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), default).AsTask());
            Assert.Equal(0, connections);
        }

        [Fact]
        public async Task Empty_DNS_result_fails_closed()
        {
            var connection = Build(_ => Task.FromResult(Array.Empty<IPAddress>()),
                (_, _, _) => throw new InvalidOperationException("Must not connect"));
            await Assert.ThrowsAsync<SocketException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), default).AsTask());
        }

        [Fact]
        public async Task Cancellation_is_preserved()
        {
            var connection = Build(_ => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }),
                (_, _, _) => throw new InvalidOperationException("Must not connect"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connection.ConnectAsync(new DnsEndPoint("example.test", 443), cancellation.Token).AsTask());
        }

        [Fact]
        public void Handler_disables_redirects_and_proxies_without_overriding_TLS_validation()
        {
            using var handler = Build(_ => Task.FromResult(Array.Empty<IPAddress>()),
                (_, _, _) => throw new InvalidOperationException()).CreateHandler();
            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseProxy);
            Assert.NotNull(handler.ConnectCallback);
            Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        }

        private static ValidatedHttpConnection Build(Func<string, Task<IPAddress[]>> resolve,
            Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect) =>
            new(new EndpointSecurityPolicy(), Context, new EndpointSecurityOption(),
                (host, _) => resolve(host), connect);
    }
}
