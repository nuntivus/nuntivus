namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryReplayWorkerResult(
        int ClaimedJobs,
        int CompletedJobs,
        int FailedJobs,
        int TotalReplayedDeliveries);
}
