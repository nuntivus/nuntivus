using System.Text.RegularExpressions;
using System.Text.Json;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Application.Events
{
    internal static class EventDefinitionValidation
    {
        private static readonly Regex EventTypePattern = new(
            "^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)*$",
            RegexOptions.Compiled);

        public const int EventTypeMaxLength = 128;
        public const int DisplayNameMaxLength = 200;
        public const int DescriptionMaxLength = 2000;
        public const int SchemaMaxLength = 1_000_000;

        public static bool IsValidEventType(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.Length <= EventTypeMaxLength
            && EventTypePattern.IsMatch(value);

        public static bool IsValidDisplayName(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.Trim().Length <= DisplayNameMaxLength;

        public static bool IsValidDescription(string? value) =>
            value is null || value.Trim().Length <= DescriptionMaxLength;

        public static bool IsValidSchemaVersion(int value) => value > 0;

        public static bool IsValidSchemaDocument(string? schema)
        {
            if (string.IsNullOrWhiteSpace(schema) || schema.Length > SchemaMaxLength)
                return false;

            try
            {
                using var _ = JsonDocument.Parse(schema);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsValidCompatibilityMode(SchemaCompatibilityMode mode) =>
            mode == SchemaCompatibilityMode.Backward || mode == SchemaCompatibilityMode.Forward;
    }
}