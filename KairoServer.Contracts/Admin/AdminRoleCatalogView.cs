using System.Collections.Generic;

namespace KairoServer.Contracts.Admin
{
    public sealed record AdminRoleCatalogView(
        IReadOnlyList<AdminRoleView> Roles);
}