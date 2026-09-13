namespace KairoServer.Infastructure.Deliveries.Http
{
    public sealed class DestinationCredentialOption
    {
        /// <summary>
        /// Optional base64-encoded 32-byte key used to encrypt/decrypt protected destination credentials.
        /// </summary>
        public string? EncryptionKey { get; set; }
    }
}
