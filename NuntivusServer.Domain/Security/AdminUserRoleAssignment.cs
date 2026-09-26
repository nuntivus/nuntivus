using System;
using System.Collections.Generic;

namespace NuntivusServer.Domain.Security
{
    public sealed class AdminUserRoleAssignment
    {
        public AdminUserRoleAssignment(
            string subject,
            string? displayName,
            IReadOnlyList<string> roles,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Subject is required.", nameof(subject));

            Subject = subject.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            Roles = roles ?? Array.Empty<string>();
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public string Subject { get; }
        public string? DisplayName { get; }
        public IReadOnlyList<string> Roles { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; }
    }
}