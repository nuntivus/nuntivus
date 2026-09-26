using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record AdminRoleCatalogView(
        IReadOnlyList<AdminRoleView> Roles);
}