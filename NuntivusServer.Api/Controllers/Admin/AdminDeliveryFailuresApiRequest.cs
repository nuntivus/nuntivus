using System;
using NuntivusServer.Api.Common;

namespace NuntivusServer.Api.Controllers.Admin
{
    public sealed class AdminDeliveryFailuresApiRequest : PagedRequest
    {
        public Guid? EventId { get; set; }

        public string? CustomerId { get; set; }

        public Guid? ContractId { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
