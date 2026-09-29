using System;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.UnitTests.Sources
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
        public void Source_lifecycle_preserves_creation_time_and_updates_state()
        {
            var now = DateTimeOffset.UtcNow;
            var source = Source.Create(Guid.NewGuid().ToString("D"), "HTTP", SourceType.HTTP_API, "{}", true, now: now);
            source.Disable(now.AddMinutes(1));
            Assert.False(source.Enabled);
            source.Enable(now.AddMinutes(2));
            source.Update("Updated", null, "{\"authentication\":{\"type\":\"none\"}}", now.AddMinutes(3));
            Assert.Equal(now, source.CreatedAt);
            Assert.Equal(now.AddMinutes(3), source.UpdatedAt);
            Assert.Equal("Updated", source.Name);
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
