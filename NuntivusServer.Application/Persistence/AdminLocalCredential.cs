using System;

namespace NuntivusServer.Application.Persistence
{
    public sealed record AdminLocalCredential(
        string Subject,
        string PasswordHash,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
