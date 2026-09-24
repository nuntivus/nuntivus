using System;
using KairoServer.Api.Common;

namespace KairoServer.Api.Controllers.Admin
{
    public sealed class AuditLogsListApiRequest : PagedRequest
    {
        public string? Category { get; set; }

        public string? Action { get; set; }

        public string? ActorId { get; set; }

        public string? CustomerId { get; set; }

        public Guid? ContractId { get; set; }

        public Guid? DeliveryId { get; set; }

        public Guid? EventId { get; set; }

        public Guid? OperationId { get; set; }

        public DateTimeOffset? CreatedFrom { get; set; }

        public DateTimeOffset? CreatedTo { get; set; }

        public string? SortDirection { get; set; }
    }
}