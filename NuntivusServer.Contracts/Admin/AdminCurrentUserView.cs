using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record AdminCurrentUserView(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions);
}