using System;
using KairoServer.Api.Common;

namespace KairoServer.Api.Controllers.Admin
{
    public sealed class AdminContractsListApiRequest : PagedRequest
    {
        public string? CustomerId { get; set; }

        public bool? Enabled { get; set; }

        public string? DestinationType { get; set; }

        public string? EventType { get; set; }

        public string? Search { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
