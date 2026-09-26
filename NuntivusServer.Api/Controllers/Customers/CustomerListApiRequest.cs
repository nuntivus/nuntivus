using System;
using NuntivusServer.Api.Common;

namespace NuntivusServer.Api.Controllers.Customers
{
    public sealed class CustomerListApiRequest : PagedRequest
    {
        public bool? Enabled { get; set; }

        public string? Search { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
