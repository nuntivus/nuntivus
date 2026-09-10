using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Outbox
{
    public class OutboxMessageType
    {
        public const string EventAccepted = "event.accepted";
        public const string DeliveryReady = "delivery.ready";
    }
}
