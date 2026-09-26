using System;

namespace NuntivusServer.Contracts.Customers
{
    public sealed record CustomerCreated(
        string Id,
        string Name,
        bool Enabled,
        DateTimeOffset CreatedAt);
}
