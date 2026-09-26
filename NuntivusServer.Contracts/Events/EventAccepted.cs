using NuntivusServer.Domain.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Contracts.Events
{

    public sealed record EventAccepted(
        Guid EventId,
        string EventType,
        string SourceId,
        DateTimeOffset OccurredAt);

}
