using NuntivusServer.Infastructure.Messaging.Nats;

namespace NuntivusServer.UnitTests.Messaging.Nats
{
    public sealed class NuntivusDurableConsumerNamerTests
    {
        [Fact]
        public void GetDurableName_adds_prefix_and_normalizes_special_characters()
        {
            var namer = new NuntivusDurableConsumerNamer();

            var durable = namer.GetDurableName("Contract.Matcher", null);

            Assert.Equal("nuntivus-contract-matcher", durable);
        }

        [Fact]
        public void GetDurableName_keeps_prefixed_name()
        {
            var namer = new NuntivusDurableConsumerNamer();

            var durable = namer.GetDurableName("delivery-dispatcher", "nuntivus-custom-durable");

            Assert.Equal("nuntivus-custom-durable", durable);
        }
    }
}
