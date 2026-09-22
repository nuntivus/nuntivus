using System;

namespace KairoServer.Application.Settings
{
    public sealed record UserPreferencesState(
        string Theme,
        int DefaultPageSize,
        bool DenseTables,
        DateTimeOffset UpdatedAt);
}