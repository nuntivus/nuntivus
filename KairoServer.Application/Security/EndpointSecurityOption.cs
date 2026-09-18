namespace KairoServer.Application.Security
{
    public class EndpointSecurityOption
    {
        public bool AllowHttpScheme { get; set; } = true;

        public bool AllowPrivateNetworkDestinations { get; set; }

        public bool AllowRedirects { get; set; }

        public int MaxResponseBodyBytes { get; set; } = 1024;
    }
}