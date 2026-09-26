namespace NuntivusServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Coarse failure taxonomy the Delivery Core uses to decide retry vs.
    /// terminal disposition. Destination executors classify their transport
    /// errors into one of these categories; Delivery Core owns retry policy.
    /// </summary>
    public enum DeliveryFailureCategory
    {
        /// <summary>Retryable failure with unknown/unspecified cause (network hiccup, 5xx, etc.).</summary>
        Transient = 0,

        /// <summary>Non-retryable failure (bad request, auth rejected, resource gone).</summary>
        Permanent = 1,

        /// <summary>Retry after the destination-suggested delay (429 / Retry-After).</summary>
        Throttled = 2,

        /// <summary>Timed out waiting for the destination.</summary>
        Timeout = 3,

        /// <summary>Destination configuration is invalid or cannot be interpreted. Non-retryable.</summary>
        ConfigurationInvalid = 4,
    }
}
