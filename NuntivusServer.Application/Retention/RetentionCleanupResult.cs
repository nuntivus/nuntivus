namespace NuntivusServer.Application.Retention
{
    public sealed record RetentionCleanupResult(
        int DeletedProcessedOutbox,
        int DeletedDeliveryAttempts,
        int DeletedDeliveries,
        int DeletedEvents,
        int DeletedAuditLogs,
        int BatchesProcessed)
    {
        public int TotalDeletedRows =>
            DeletedProcessedOutbox + DeletedDeliveryAttempts + DeletedDeliveries + DeletedEvents + DeletedAuditLogs;
    }
}
