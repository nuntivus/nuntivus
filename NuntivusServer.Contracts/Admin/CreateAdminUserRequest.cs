using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record CreateAdminUserRequest(
        string Subject,
        string? DisplayName,
    IReadOnlyList<string> Roles,
    string? Password = null);
}
