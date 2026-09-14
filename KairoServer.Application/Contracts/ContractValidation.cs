using KairoServer.Application.Contracts.Destinations;
using KairoServer.Contracts.CustomerContracts;
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
        public const int MaxFilterRulesPerScope = 50;
        public const int MaxTransformationMappings = 100;
        public const int FilterSelectorMaxLength = 200;
        public const int FilterValueMaxLength = 500;

        private static readonly Regex FilterSelectorPattern = new(
            "^[A-Za-z0-9_-]+(\\.[A-Za-z0-9_-]+)*$",
            RegexOptions.Compiled);

        public static bool IsValidEventType(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.Length <= EventTypeMaxLength
            && EventTypePattern.IsMatch(value);

        public static bool IsValidSchemaVersion(int version) => version > 0;

        public static bool IsValidFilterSelector(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.Length <= FilterSelectorMaxLength
            && FilterSelectorPattern.IsMatch(value);

        public static bool IsValidFilterOperator(ContractFilterOperator value) =>
            System.Enum.IsDefined(value);

        public static bool IsSetOperator(ContractFilterOperator value) =>
            value == ContractFilterOperator.In || value == ContractFilterOperator.NotIn;

        public static bool IsStringOperator(ContractFilterOperator value) =>
            value == ContractFilterOperator.Contains
            || value == ContractFilterOperator.StartsWith
            || value == ContractFilterOperator.EndsWith;
    }
}
