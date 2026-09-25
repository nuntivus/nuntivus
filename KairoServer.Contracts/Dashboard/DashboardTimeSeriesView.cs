using System;
using System.Collections.Generic;

namespace KairoServer.Contracts.Dashboard
{
    public sealed record DashboardTimeSeriesView(
        DateTimeOffset AsOf,
        int RangeSeconds,
        IReadOnlyList<DashboardTimeBucketView> Buckets);

    public sealed record DashboardTimeBucketView(
        DateTimeOffset Start,
        long Events,
        long Deliveries,
        long Delivered,
        long Failed,
        long Dead);
}
