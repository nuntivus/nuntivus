using System;
using KairoServer.Application.Security;

namespace KairoServer.Api.Security
{
    public interface IAdminTokenIssuer
    {
        string IssueToken(AdminAuthenticatedIdentity identity, DateTimeOffset issuedAtUtc);
    }
}
