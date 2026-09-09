using KairoServer.Application.Contracts.Destinations;
using System.Text.RegularExpressions;

namespace KairoServer.Application.Contracts
{
    /// <summary>
    /// Shared validation helpers for Contract input.
    /// Destination configuration validation lives in per-type
    /// <see cref="IDestinationConfigHandler"/> implementations.
    /// </summary>
    internal static class ContractValidation
    {
        // Event type: dotted identifier segments, e.g. "orders.created".
        private static readonly Regex EventTypePattern = new(
            "^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)*$",
            RegexOptions.Compiled);

        public const int EventTypeMaxLength = 128;
        public const int MaxEventTypesPerContract = 100;

        public static bool IsValidEventType(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.Length <= EventTypeMaxLength
            && EventTypePattern.IsMatch(value);
    }
}
