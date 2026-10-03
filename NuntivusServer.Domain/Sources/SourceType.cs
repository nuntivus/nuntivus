namespace NuntivusServer.Domain.Sources
{
    /// <summary>
    /// Categorizes the ingestion adapter that produces events for a <see cref="Source"/>.
    /// New values should be added as new adapter families are introduced.
    /// </summary>
    public enum SourceType
    {
        HTTP_API,
        GENERIC_WEBHOOK,
        SDK_APPLICATION,
        STRIPE_WEBHOOK_INTEGRATION,
        KAFKA,
        NATS,
        RABBITMQ,
        SQS,
        HTTP_POLLING,
        FOLDER,
        S3
    }
}
