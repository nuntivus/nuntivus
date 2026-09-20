namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record SqsDestinationConfig(
        string Region,
        string QueueUrl,
        string? ServiceUrl = null,
        int? TimeoutSeconds = null,
        SqsCredentials? Credentials = null) : DestinationConfig;

    public sealed record SqsCredentials(
        string? AccessKeyId = null,
        DestinationCredential? SecretAccessKey = null,
        DestinationCredential? SessionToken = null);
}
