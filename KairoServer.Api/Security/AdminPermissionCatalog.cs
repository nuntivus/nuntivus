using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace KairoServer.Api.Security
{
    public static class AdminPermissionCatalog
    {
        private static readonly string[] ViewerPermissions =
        [
            "dashboard.overview.read",
            "customers.read",
            "contracts.read",
            "events.read",
            "deliveries.read",
            "delivery-failures.read",
            "destination-health.read",
            "event-definitions.read",
            "users.self.read",
            "roles.read",
        ];

        private static readonly string[] OperatorOnlyPermissions =
        [
            "customers.write",
            "contracts.write",
            "contracts.credentials.write",
            "events.publish",
            "events.replay",
            "deliveries.operate",
            "deliveries.replay-jobs.write",
            "event-definitions.write",
        ];

        private static readonly string[] AdminOnlyPermissions =
        [
            "users.read",
            "roles.manage",
            "settings.write",
        ];

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions =
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [AdminAuthorizationPolicies.ViewerRole] = ViewerPermissions,
                    [AdminAuthorizationPolicies.OperatorRole] = ViewerPermissions.Concat(OperatorOnlyPermissions).ToArray(),
                    [AdminAuthorizationPolicies.AdminRole] = ViewerPermissions.Concat(OperatorOnlyPermissions).Concat(AdminOnlyPermissions).ToArray(),
                });

        public static IReadOnlyList<string> NormalizeRoles(IEnumerable<string> roles)
        {
            if (roles is null)
                return Array.Empty<string>();

            return roles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Where(RolePermissions.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<string> GetPermissionsForRoles(IEnumerable<string> roles)
        {
            if (roles is null)
                return Array.Empty<string>();

            return roles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Where(RolePermissions.ContainsKey)
                .SelectMany(static role => RolePermissions[role])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<AdminRoleDescriptor> GetRoleCatalog()
        {
            return
            [
                new AdminRoleDescriptor(
                    AdminAuthorizationPolicies.ViewerRole,
                    "Read-only operational access for dashboard users.",
                    ViewerPermissions),
                new AdminRoleDescriptor(
                    AdminAuthorizationPolicies.OperatorRole,
                    "Operational access including delivery and contract actions.",
                    ViewerPermissions.Concat(OperatorOnlyPermissions).ToArray()),
                new AdminRoleDescriptor(
                    AdminAuthorizationPolicies.AdminRole,
                    "Platform administration access including role and settings management.",
                    ViewerPermissions.Concat(OperatorOnlyPermissions).Concat(AdminOnlyPermissions).ToArray()),
            ];
        }

        public sealed record AdminRoleDescriptor(
            string Name,
            string Description,
            IReadOnlyList<string> Permissions);
    }
}