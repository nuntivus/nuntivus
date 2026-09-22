using System;
using System.Collections.Generic;

namespace KairoServer.Contracts.Admin
{
    public sealed record AdminUserRoleAssignmentView(
        string Subject,
        string? DisplayName,
        IReadOnlyList<string> Roles,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}