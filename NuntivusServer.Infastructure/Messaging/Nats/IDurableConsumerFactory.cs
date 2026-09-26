using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Messaging.Nats
{
    public interface IDurableConsumerFactory
    {
        Task<DurableConsumerHandle> CreateOrUpdateAsync(
            DurableConsumerSpec spec,
            CancellationToken cancellationToken);
    }
}