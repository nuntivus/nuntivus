using System;
using KairoServer.Domain.Sources;

namespace KairoServer.UnitTests.Sources
{
    public class SourceIdTests
    {
        [Theory]
        [InlineData("stripe-webhook", "stripe-webhook")]
        [InlineData("  Stripe-Webhook  ", "stripe-webhook")]
        [InlineData("orders.v1", "orders.v1")]
        [InlineData("HTTP_API_1", "http_api_1")]
        public void Create_Normalizes_Value(string input, string expected)
        {
            var id = SourceId.Create(input);
            Assert.Equal(expected, id.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ab")]                          // too short (needs 3+)
        [InlineData("-abc")]                        // must start with alphanum
        [InlineData("abc-")]                        // must end with alphanum
        [InlineData("abc def")]                     // whitespace inside
        [InlineData("abc$def")]                     // invalid char
        public void Create_Throws_On_Invalid(string? input)
        {
            Assert.Throws<ArgumentException>(() => SourceId.Create(input!));
        }

        [Fact]
        public void Create_Rejects_TooLong()
        {
            var input = new string('a', 65);
            Assert.Throws<ArgumentException>(() => SourceId.Create(input));
        }

        [Fact]
        public void Equality_Is_Case_Insensitive_Via_Normalization()
        {
            var a = SourceId.Create("Orders.V1");
            var b = SourceId.Create("orders.v1");

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Implicit_String_Returns_Normalized_Value()
        {
            var id = SourceId.Create("Orders.V1");
            string s = id;
            Assert.Equal("orders.v1", s);
        }
    }
}
