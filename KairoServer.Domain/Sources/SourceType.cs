using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Sources
{
    public enum SourceType
    {
        HTTP_API,
        SDK_APPLICATION,
        STRIPE_WEBHOOK_INTEGRATION,
        KAFKA,
        NATS,
        RABBITMQ,
        SQS
    }
}
