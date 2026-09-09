using System;
using System.Text.RegularExpressions;

namespace KairoServer.Common
{
    /// <summary>
    /// A scheme-prefixed dotted reference key used to indirect configuration values
    /// through a resolver (e.g. <c>secret.acme.webhook</c>,
    /// <c>dynamicvariable.tenant.region</c>). Only the schemes explicitly listed
    /// on <see cref="TryParse"/> callsites are accepted.
    /// </summary>
    public readonly record struct ConfigReference(string Scheme, string Path)
    {
        // scheme: lowercase letters only. path: one or more dot-separated segments
        // of alphanumerics, '_' or '-'.
        private static readonly Regex Pattern = new(
            "^(?<scheme>[a-z]+)(?<path>(?:\\.[a-zA-Z0-9_-]+)+)$",
            RegexOptions.Compiled);

        public const int MaxLength = 256;

        public override string ToString() => $"{Scheme}{Path}";

        public static bool TryParse(string? value, out ConfigReference reference)
        {
            reference = default;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
                return false;

            var m = Pattern.Match(value);
            if (!m.Success)
                return false;

            reference = new ConfigReference(
                m.Groups["scheme"].Value,
                m.Groups["path"].Value);
            return true;
        }

        /// <summary>
        /// Parses <paramref name="value"/> and enforces that its scheme matches
        /// <paramref name="expectedScheme"/>.
        /// </summary>
        public static bool TryParse(string? value, string expectedScheme, out ConfigReference reference)
        {
            if (!TryParse(value, out reference))
                return false;

            if (!string.Equals(reference.Scheme, expectedScheme, StringComparison.Ordinal))
            {
                reference = default;
                return false;
            }
            return true;
        }
    }
}
