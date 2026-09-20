using System;
using KairoServer.Api.Common;

namespace KairoServer.Api.Controllers.Contracts
{
    public sealed class ContractListApiRequest : PagedRequest
    {
        public bool? Enabled { get; set; }

        public string? EventType { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
