using System;
using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractCreated(
        Guid Id,
        string CustomerId,
        bool Enabled,
        IReadOnlyCollection<string> EventTypes,
        DestinationType DestinationType,
        DateTimeOffset CreatedAt);
}
