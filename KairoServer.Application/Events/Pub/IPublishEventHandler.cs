using KairoServer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Events.Pub
{
    public interface IPublishEventHandler
    {
        Task<Result<PublishEventResult>> HandleAsync(
            PublishEventCommand command,
            CancellationToken cancellationToken);
    }
}
