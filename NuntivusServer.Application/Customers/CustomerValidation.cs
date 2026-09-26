using System.Text.RegularExpressions;

namespace NuntivusServer.Application.Customers
{
    /// <summary>
    /// Shared validation helpers for Customer input.
    /// </summary>
    internal static class CustomerValidation
    {
        // External identifier: lowercase alphanumerics, hyphens, underscores; 3-64 chars.
        private static readonly Regex IdPattern = new(
            "^[a-z0-9][a-z0-9_-]{2,63}$",
            RegexOptions.Compiled);

        public const int NameMaxLength = 200;

        public static bool IsValidId(string? id) =>
            !string.IsNullOrWhiteSpace(id) && IdPattern.IsMatch(id);

        public static bool IsValidName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= NameMaxLength;
    }
}
