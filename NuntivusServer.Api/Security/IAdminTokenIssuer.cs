using System;
using NuntivusServer.Application.Security;

namespace NuntivusServer.Api.Security
{
    public interface IAdminTokenIssuer
    {
        string IssueToken(AdminAuthenticatedIdentity identity, DateTimeOffset issuedAtUtc);
    }
}
