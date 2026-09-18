using System;
using System.Net;

namespace KairoServer.Application.Contracts.Destinations
{
    public static class HttpDestinationSecurityPolicy
    {
        public static bool TryValidateDestinationUri(
            string url,
            HttpDestinationSecurityOption option,
            out Uri uri,
            out string error)
        {
            uri = default!;
            error = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                error = "Destination url must be an absolute URL.";
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                return ValidateHostPolicy(uri, option, out error);
            }

            if (uri.Scheme == Uri.UriSchemeHttp && option.AllowHttpScheme)
            {
                return ValidateHostPolicy(uri, option, out error);
            }

            error = option.AllowHttpScheme
                ? "Destination url must use http or https scheme."
                : "Destination url must use https scheme.";
            return false;
        }

        private static bool ValidateHostPolicy(
            Uri uri,
            HttpDestinationSecurityOption option,
            out string error)
        {
            error = string.Empty;

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                error = "Destination url must not include inline user info.";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                error = "Destination url must not include fragments.";
                return false;
            }

            var host = uri.DnsSafeHost;
            if (string.IsNullOrWhiteSpace(host))
            {
                error = "Destination url host is required.";
                return false;
            }

            if (!option.AllowPrivateNetworkDestinations)
            {
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Destination url host is not allowed by private-network policy.";
                    return false;
                }

                if (IPAddress.TryParse(host, out var ip) && IsPrivateNetworkAddress(ip))
                {
                    error = "Destination url host is not allowed by private-network policy.";
                    return false;
                }
            }

            return true;
        }

        public static bool IsPrivateNetworkAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
                return true;

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return true;

                var bytes = address.GetAddressBytes();
                if (bytes.Length == 16)
                {
                    var first = bytes[0];
                    // fc00::/7 unique local addresses.
                    if ((first & 0b1111_1110) == 0b1111_1100)
                        return true;
                }

                return false;
            }

            var v4 = address.GetAddressBytes();
            if (v4.Length != 4)
                return false;

            // RFC1918 + link local + CGNAT + reserved blocks commonly abused in SSRF.
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