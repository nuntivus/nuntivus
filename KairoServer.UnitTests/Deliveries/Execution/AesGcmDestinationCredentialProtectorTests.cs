using KairoServer.Infastructure.Deliveries.Http;

namespace KairoServer.UnitTests.Deliveries.Execution
{
    public sealed class AesGcmDestinationCredentialProtectorTests
    {
        [Fact]
        public void Constructor_without_key_throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _ = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption()));

            Assert.Contains("encryption key is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Constructor_with_invalid_key_length_throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _ = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption
                {
                    EncryptionKey = Convert.ToBase64String(new byte[16])
                }));

            Assert.Contains("exactly 32 bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
