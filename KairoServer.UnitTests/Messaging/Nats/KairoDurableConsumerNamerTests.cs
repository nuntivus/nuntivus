using KairoServer.Infastructure.Messaging.Nats;

namespace KairoServer.UnitTests.Messaging.Nats
{
    public sealed class KairoDurableConsumerNamerTests
    {
        [Fact]
        public void GetDurableName_adds_prefix_and_normalizes_special_characters()
        {
            var namer = new KairoDurableConsumerNamer();

            var durable = namer.GetDurableName("Contract.Matcher", null);

            Assert.Equal("kairo-contract-matcher", durable);
        }

        [Fact]
        public void GetDurableName_keeps_prefixed_name()
        {
            var namer = new KairoDurableConsumerNamer();

            var durable = namer.GetDurableName("delivery-dispatcher", "kairo-custom-durable");

            Assert.Equal("kairo-custom-durable", durable);
        }
    }
}
