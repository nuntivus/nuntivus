using System.Collections.Generic;
using System.Net;
using KairoServer.Common;

namespace KairoServer.Application.Security
{
    public interface IEndpointSecurityPolicy
    {
        Result ValidateEndpointUri(
            Uri endpointUri,
            EndpointSecurityContext context,
            EndpointSecurityOption options);

        Result ValidateResolvedAddresses(
            IReadOnlyCollection<IPAddress> addresses,
            EndpointSecurityContext context,
            EndpointSecurityOption options);

        bool IsPrivateNetworkAddress(IPAddress address);
    }
}