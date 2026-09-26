using System.Collections.Generic;

namespace NuntivusServer.Application.Security
{
    public sealed record AdminAuthenticatedIdentity(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles);
}
