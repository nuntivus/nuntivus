using System;
using System.Collections.Generic;

namespace NuntivusServer.Contracts.Admin
{
    public sealed record AdminUserRoleAssignmentView(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}