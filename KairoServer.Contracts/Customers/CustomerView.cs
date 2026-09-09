using System;

namespace KairoServer.Contracts.Customers
{
    public sealed record CustomerView(
        string Id,
        string Name,
        bool Enabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
