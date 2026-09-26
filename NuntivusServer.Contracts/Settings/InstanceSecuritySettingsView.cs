namespace NuntivusServer.Contracts.Settings
{
    public sealed record InstanceSecuritySettingsView(
        bool RequireHttpsDestinations,
        bool AllowPrivateNetworkDestinations,
        bool RequireDestinationCredentialEncryption);
}