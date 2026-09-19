using KairoServer.Application.Security;

namespace KairoServer.Application.Contracts.Destinations
{
    public sealed class HttpDestinationSecurityOption : EndpointSecurityOption
    {
        public int MaxHeaders { get; set; } = 64;

        public int MaxConfigurationBytes { get; set; } = 65_536;
    }
}