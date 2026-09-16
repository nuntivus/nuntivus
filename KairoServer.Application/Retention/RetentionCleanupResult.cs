namespace KairoServer.Application.Retention
{
    public sealed record RetentionCleanupResult(
        int DeletedProcessedOutbox,
        int DeletedDeliveryAttempts,
        int DeletedDeliveries,
        int DeletedEvents,
        int BatchesProcessed)
    {
        public int TotalDeletedRows =>
            DeletedProcessedOutbox + DeletedDeliveryAttempts + DeletedDeliveries + DeletedEvents;
    }
}
