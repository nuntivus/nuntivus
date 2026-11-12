using NuntivusServer.Application.Exports;

namespace NuntivusServer.UnitTests.Exports
{
    public sealed class DataExportFormatterTests
    {
        [Theory]
        [InlineData("=1+1", "\"'=1+1\"")]
        [InlineData(" +SUM(A1)", "\"' +SUM(A1)\"")]
        [InlineData("@command", "\"'@command\"")]
        [InlineData("-1", "\"'-1\"")]
        [InlineData("a\nb", "\"'a\nb\"")]
        [InlineData("a,\"b", "\"a,\"\"b\"")]
        public void Csv_quotes_and_neutralizes_untrusted_text(string value, string expected) => Assert.Equal(expected, DataExportFormatter.CsvCell(value));
        [Fact]
        public void Csv_preserves_numeric_types() => Assert.Equal("\"-1\"", DataExportFormatter.CsvCell(-1));
    }
}
