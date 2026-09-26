using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record AdminRoleView(
        string Name,
        string Description,
        IReadOnlyList<string> Permissions);
}