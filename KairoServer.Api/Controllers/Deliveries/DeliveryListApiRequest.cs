using System;
using KairoServer.Api.Common;

namespace KairoServer.Api.Controllers.Deliveries
{
    public sealed class DeliveryListApiRequest : PagedRequest
    {
        public Guid? EventId { get; set; }

        public string? CustomerId { get; set; }

        public Guid? ContractId { get; set; }

        public string? DestinationType { get; set; }

        public string? FailureCategory { get; set; }

        public string? Status { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortBy { get; set; }

        public string? SortDirection { get; set; }
    }
}
