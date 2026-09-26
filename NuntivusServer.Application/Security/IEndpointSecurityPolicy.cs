using System.Collections.Generic;
using System.Net;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Security
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