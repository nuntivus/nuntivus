using System.Collections.Generic;

namespace KairoServer.Application.Security
{
    public sealed record AdminAuthenticatedIdentity(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles);
}
