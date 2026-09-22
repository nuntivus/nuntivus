using System.Collections.Generic;

namespace KairoServer.Contracts.Admin
{
    public sealed record AdminRoleView(
        string Name,
        string Description,
        IReadOnlyList<string> Permissions);
}