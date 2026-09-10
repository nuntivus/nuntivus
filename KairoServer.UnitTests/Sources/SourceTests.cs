using System;
using KairoServer.Domain.Sources;

namespace KairoServer.UnitTests.Sources
{
    public class SourceTests
    {
        [Fact]
        public void Create_Preserves_Properties()
        {
            var source = Source.Create(
                id: "Orders.V1",
                name: "  Orders v1  ",
                type: SourceType.HTTP_API,
                configuration: "{}",
                enabled: true,
                description: "primary orders API");

            Assert.Equal("orders.v1", source.Id.Value);
            Assert.Equal("Orders v1", source.Name);
            Assert.Equal("primary orders API", source.Description);
            Assert.Equal(SourceType.HTTP_API, source.Type);
            Assert.Equal("{}", source.Configuration);
            Assert.True(source.Enabled);
        }

        [Fact]
        public void Create_Allows_Null_Description()
        {
            var source = Source.Create("orders.v1", "Orders", SourceType.HTTP_API, "{}", true);
            Assert.Null(source.Description);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_Throws_On_Missing_Name(string? name)
        {
            Assert.Throws<ArgumentException>(() =>
                Source.Create("orders.v1", name!, SourceType.HTTP_API, "{}", true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_Throws_On_Missing_Configuration(string? config)
        {
            Assert.Throws<ArgumentException>(() =>
                Source.Create("orders.v1", "Orders", SourceType.HTTP_API, config!, true));
        }

        [Fact]
        public void Create_Throws_On_Invalid_Id()
        {
            Assert.Throws<ArgumentException>(() =>
                Source.Create("bad id!", "Orders", SourceType.HTTP_API, "{}", true));
        }
    }
}
