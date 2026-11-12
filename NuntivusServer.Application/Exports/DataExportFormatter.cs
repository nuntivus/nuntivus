using System.Globalization;

namespace NuntivusServer.Application.Exports
{
    public static class DataExportFormatter
    {
        public static string CsvCell(object? value)
        {
            var text = value switch
            {
                null => string.Empty,
                DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
            if (value is string && (text.Any(char.IsControl) || text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+')
                || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@'))) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
