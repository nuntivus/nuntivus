using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Security
{
    public sealed class EndpointSecurityPolicy : IEndpointSecurityPolicy
    {
        public Result ValidateEndpointUri(
            Uri endpointUri,
            EndpointSecurityContext context,
            EndpointSecurityOption options)
        {
            ArgumentNullException.ThrowIfNull(endpointUri);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            if (!endpointUri.IsAbsoluteUri)
            {
                return Result.Failure(Error.Validation(
                    $"{context.ComponentName} url must be an absolute URL."));
            }

            if (endpointUri.Scheme != Uri.UriSchemeHttps
                && (endpointUri.Scheme != Uri.UriSchemeHttp || !options.AllowHttpScheme))
            {
                var expected = options.AllowHttpScheme ? "http or https" : "https";
                return Result.Failure(Error.Validation(
                    $"{context.ComponentName} url must use {expected} scheme."));
            }

            if (!string.IsNullOrWhiteSpace(endpointUri.UserInfo))
            {
                return Result.Failure(Error.Validation(
                    $"{context.ComponentName} url must not include inline user info."));
            }

            if (!string.IsNullOrEmpty(endpointUri.Fragment))
            {
                return Result.Failure(Error.Validation(
                    $"{context.ComponentName} url must not include fragments."));
            }

            var host = endpointUri.DnsSafeHost;
            if (string.IsNullOrWhiteSpace(host))
            {
                return Result.Failure(Error.Validation(
                    $"{context.ComponentName} url host is required."));
            }

            if (!options.AllowPrivateNetworkDestinations)
            {
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(Error.Validation(
                        $"{context.ComponentName} url host is not allowed by private-network policy."));
                }

                if (IPAddress.TryParse(host, out var ip) && IsPrivateNetworkAddress(ip))
                {
                    return Result.Failure(Error.Validation(
                        $"{context.ComponentName} url host is not allowed by private-network policy."));
                }
            }

            return Result.Success();
        }

        public Result ValidateResolvedAddresses(
            IReadOnlyCollection<IPAddress> addresses,
            EndpointSecurityContext context,
            EndpointSecurityOption options)
        {
            ArgumentNullException.ThrowIfNull(addresses);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            if (options.AllowPrivateNetworkDestinations)
                return Result.Success();

            foreach (var address in addresses)
            {
                if (IsPrivateNetworkAddress(address))
                {
                    return Result.Failure(Error.Validation(
                        $"{context.ComponentName} host resolution is not allowed by private-network policy."));
                }
            }

            return Result.Success();
        }

        public bool IsPrivateNetworkAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
                return true;

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return true;

                var bytes = address.GetAddressBytes();
                if (bytes.Length == 16)
                {
                    var first = bytes[0];
                    if ((first & 0b1111_1110) == 0b1111_1100)
                        return true;
                }

                return false;
            }

            var v4 = address.GetAddressBytes();
            if (v4.Length != 4)
                return false;

            if (v4[0] == 10)
                return true;
            if (v4[0] == 127)
                return true;
            if (v4[0] == 169 && v4[1] == 254)
                return true;
            if (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31)
                return true;
            if (v4[0] == 192 && v4[1] == 168)
                return true;
            if (v4[0] == 100 && v4[1] >= 64 && v4[1] <= 127)
                return true;
            if (v4[0] == 0)
                return true;

            return false;
        }
    }
}