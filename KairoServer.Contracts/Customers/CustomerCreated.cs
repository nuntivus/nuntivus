using System;

namespace KairoServer.Contracts.Customers
{
    public sealed record CustomerCreated(
        string Id,
        string Name,
        bool Enabled,
        DateTimeOffset CreatedAt);
}
