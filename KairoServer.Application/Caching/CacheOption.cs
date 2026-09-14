using System;

namespace KairoServer.Application.Caching
{
    /// <summary>
    /// Cache configuration. Kept intentionally minimal: a single TTL applied
    /// uniformly to all repository decorators. Fine-grained per-namespace TTLs
    /// or invalidation strategies (poll, event bus) can be layered later.
    /// </summary>
    public sealed class CacheOption
    {
        public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Cache key namespaces used by repository decorators. Centralized so
    /// prefixes stay consistent across code and any future invalidation logic.
    /// </summary>
    public static class CacheKeys
    {
        public const string ContractsMatch = "contracts_match";
        public const string ContractsById = "contracts_by_id";
        public const string ContractsByCustomer = "contracts_by_customer";
        public const string EventsById = "events_by_id";
        public const string EventReplays = "event_replays";
        public const string CustomersById = "customers_by_id";
    }
}
