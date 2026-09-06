using KairoServer.Domain.Events;
using KairoServer.Domain.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Events.Pub
{
    public sealed record PublishEventCommand(
        string IdempotencyKey,
        string Type,
        string SourceId,
        string Payload,
        DateTimeOffset OccurredAt,
        string? Metadata = null);
}
