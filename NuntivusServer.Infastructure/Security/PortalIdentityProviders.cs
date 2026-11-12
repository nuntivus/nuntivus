using NuntivusServer.Application.Portal;

namespace NuntivusServer.Infastructure.Security
{
    public sealed class LocalPortalIdentityProvider(PortalOptions options) : IPortalIdentityProvider
    {
        public string Name => "local";
        public string Issuer => options.LocalIssuer;
        public bool Enabled => options.LocalEnabled;
        public bool SupportsPassword => true;
    }
    public sealed class ClerkPortalIdentityProvider(PortalOptions options) : IPortalIdentityProvider
    {
        public string Name => "clerk";
        public string Issuer => options.ClerkAuthority;
        public bool Enabled => options.ClerkEnabled;
        public bool SupportsPassword => false;
    }
}
