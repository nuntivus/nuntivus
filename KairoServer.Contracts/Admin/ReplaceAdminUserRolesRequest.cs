using System.Collections.Generic;

namespace KairoServer.Contracts.Admin
{
    public sealed record ReplaceAdminUserRolesRequest(
        string? DisplayName,
        IReadOnlyList<string> Roles);
}