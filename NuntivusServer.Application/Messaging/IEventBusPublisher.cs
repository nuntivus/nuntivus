using NuntivusServer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Messaging
{
    public interface IEventBusPublisher
    {
        Task<Result> PublishAsync(
            string subject,
            string payload,
            string messageId,
            CancellationToken cancellationToken);
    }
}
