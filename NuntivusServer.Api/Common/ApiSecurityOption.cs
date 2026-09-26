namespace NuntivusServer.Api.Common
{
    public sealed class ApiSecurityOption
    {
        public long MaxRequestBodyBytes { get; set; } = 1_048_576;

        public int MaxEventPayloadBytes { get; set; } = 262_144;

        public int MaxEventMetadataBytes { get; set; } = 65_536;
    }
}