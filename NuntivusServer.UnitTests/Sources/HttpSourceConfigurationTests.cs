using NuntivusServer.Application.Sources;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class HttpSourceConfigurationTests
    {
        [Theory]
        [InlineData("{}")]
        [InlineData("{\"authentication\":{\"type\":\"token\",\"value\":\"plaintext\"}}")]
        [InlineData("{\"authentication\":{\"type\":\"token\",\"secretRef\":\"plaintext\"}}")]
        [InlineData("{\"authentication\":{\"type\":\"none\",\"secretRef\":\"secret.test\"}}")]
        public void RejectsInvalidOrInlineCredentials(string configuration) =>
            Assert.True(HttpSourceConfigurationValidator.Normalize(configuration).IsFailure);

        [Fact]
        public void AcceptsManagedSecretReference() =>
            Assert.True(HttpSourceConfigurationValidator.Normalize(
                "{\"authentication\":{\"type\":\"token\",\"secretRef\":\"secret.test.source\"}}")
                .IsSuccess);

        [Fact]
        public async Task HttpAdapterDoesNotCopySensitiveMetadata()
        {
            var adapter = new HttpApiSourceAdapter();
            var result = await adapter.NormalizeAsync(new HttpApiSourceEventContext("idem", "type",
                Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, "{}",
                "{\"authorization\":\"Bearer private\"}"), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.DoesNotContain("private", result.Error!.Message);
        }
    }
}
