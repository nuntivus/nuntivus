using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record ReplaceAdminUserRolesRequest(
        string? DisplayName,
    IReadOnlyList<string> Roles,
    string? Password = null);
}