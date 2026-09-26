using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Application.Matching
{
    public sealed class ContractFilterEvaluator : IContractFilterEvaluator
    {
        public Result<bool> Evaluate(ContractFilter? filter, Event @event)
        {
            if (filter is null || !filter.HasRules)
                return Result.Success(true);

            JsonDocument? payload = null;
            try
            {
                if (filter.PayloadRules.Count > 0)
                    payload = JsonDocument.Parse(@event.Payload);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(Error.Validation(
                    $"Event '{@event.Id}' payload could not be parsed for contract filter evaluation. {ex.Message}"));
            }

            try
            {
                foreach (var rule in filter.MetadataRules)
                {
                    if (@event.Metadata is null
                        || !@event.Metadata.TryGetValue(rule.Selector, out var metadataValue)
                        || !EvaluateStringRule(metadataValue, rule))
                    {
                        return Result.Success(false);
                    }
                }

                if (payload is null)
                    return Result.Success(true);

                foreach (var rule in filter.PayloadRules)
                {
                    if (!TryResolvePayloadValue(payload.RootElement, rule.Selector, out var value)
                        || !EvaluateJsonRule(value, rule))
                    {
                        return Result.Success(false);
                    }
                }

                return Result.Success(true);
            }
            finally
            {
                payload?.Dispose();
            }
        }

        private static bool EvaluateStringRule(string actual, ContractFilterRule rule)
        {
            return rule.Operator switch
            {
                ContractFilterOperator.Equals => string.Equals(actual, rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.NotEquals => !string.Equals(actual, rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.Contains => rule.Value is not null && actual.Contains(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.StartsWith => rule.Value is not null && actual.StartsWith(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.EndsWith => rule.Value is not null && actual.EndsWith(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.In => rule.Values.Contains(actual, StringComparer.Ordinal),
                ContractFilterOperator.NotIn => !rule.Values.Contains(actual, StringComparer.Ordinal),
                ContractFilterOperator.GreaterThan => TryCompareAsDecimal(actual, rule.Value, out var gt) && gt > 0,
                ContractFilterOperator.GreaterThanOrEqual => TryCompareAsDecimal(actual, rule.Value, out var gte) && gte >= 0,
                ContractFilterOperator.LessThan => TryCompareAsDecimal(actual, rule.Value, out var lt) && lt < 0,
                ContractFilterOperator.LessThanOrEqual => TryCompareAsDecimal(actual, rule.Value, out var lte) && lte <= 0,
                _ => false
            };
        }

        private static bool EvaluateJsonRule(JsonElement actual, ContractFilterRule rule)
        {
            return rule.Operator switch
            {
                ContractFilterOperator.Equals => Equals(actual, rule.Value),
                ContractFilterOperator.NotEquals => !Equals(actual, rule.Value),
                ContractFilterOperator.GreaterThan => Compare(actual, rule.Value, out var gt) && gt > 0,
                ContractFilterOperator.GreaterThanOrEqual => Compare(actual, rule.Value, out var gte) && gte >= 0,
                ContractFilterOperator.LessThan => Compare(actual, rule.Value, out var lt) && lt < 0,
                ContractFilterOperator.LessThanOrEqual => Compare(actual, rule.Value, out var lte) && lte <= 0,
                ContractFilterOperator.Contains => actual.ValueKind == JsonValueKind.String
                    && rule.Value is not null
                    && actual.GetString()!.Contains(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.StartsWith => actual.ValueKind == JsonValueKind.String
                    && rule.Value is not null
                    && actual.GetString()!.StartsWith(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.EndsWith => actual.ValueKind == JsonValueKind.String
                    && rule.Value is not null
                    && actual.GetString()!.EndsWith(rule.Value, StringComparison.Ordinal),
                ContractFilterOperator.In => rule.Values.Any(v => Equals(actual, v)),
                ContractFilterOperator.NotIn => !rule.Values.Any(v => Equals(actual, v)),
                _ => false
            };
        }

        private static bool TryResolvePayloadValue(JsonElement root, string selector, out JsonElement value)
        {
            value = root;
            var current = root;
            foreach (var segment in selector.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object
                    || !current.TryGetProperty(segment, out current))
                {
                    return false;
                }
            }

            value = current;
            return true;
        }

        private static bool Equals(JsonElement actual, string? expected)
        {
            switch (actual.ValueKind)
            {
                case JsonValueKind.String:
                    return string.Equals(actual.GetString(), expected, StringComparison.Ordinal);
                case JsonValueKind.Number:
                    return TryParseDecimal(expected, out var expectedNumber)
                        && actual.TryGetDecimal(out var actualNumber)
                        && actualNumber == expectedNumber;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return bool.TryParse(expected, out var expectedBool)
                        && actual.GetBoolean() == expectedBool;
                case JsonValueKind.Null:
                    return string.Equals(expected, "null", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static bool Compare(JsonElement actual, string? expected, out int comparison)
        {
            comparison = 0;
            if (actual.ValueKind != JsonValueKind.Number
                || !actual.TryGetDecimal(out var actualNumber)
                || !TryParseDecimal(expected, out var expectedNumber))
            {
                return false;
            }

            comparison = actualNumber.CompareTo(expectedNumber);
            return true;
        }

        private static bool TryCompareAsDecimal(string actual, string? expected, out int comparison)
        {
            comparison = 0;
            if (!TryParseDecimal(actual, out var left) || !TryParseDecimal(expected, out var right))
                return false;

            comparison = left.CompareTo(right);
            return true;
        }

        private static bool TryParseDecimal(string? value, out decimal parsed) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
    }
}