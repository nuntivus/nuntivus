using System;

namespace NuntivusServer.Contracts.Settings
{
    public sealed record UserPreferencesSettingsView(
        string Theme,
        int DefaultPageSize,
        bool DenseTables,
        DateTimeOffset UpdatedAt);
}