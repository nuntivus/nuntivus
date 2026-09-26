using System;
using NuntivusServer.Api.Common;

namespace NuntivusServer.Api.Controllers.Events
{
    public sealed class EventListApiRequest : PagedRequest
    {
        public string? Type { get; set; }

        public string? Source { get; set; }

        public DateTimeOffset? OccurredFrom { get; set; }

        public DateTimeOffset? OccurredTo { get; set; }

        public DateTimeOffset? ReceivedFrom { get; set; }

        public DateTimeOffset? ReceivedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
