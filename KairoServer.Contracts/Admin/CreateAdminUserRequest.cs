using System.Collections.Generic;

namespace KairoServer.Contracts.Admin
{
    public sealed record CreateAdminUserRequest(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles);
}
