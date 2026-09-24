using System;

namespace KairoServer.Application.Persistence
{
    public sealed record AdminLocalCredential(
        string Subject,
        string PasswordHash,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
