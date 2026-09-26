using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Security
{
    public sealed class PostgresAdminAuthenticationProvider : IAdminAuthenticationProvider
    {
        private static readonly Error InvalidCredentials = Error.Unauthorized("Invalid username or password.");

        private readonly IAdminLocalCredentialRepository _credentials;
        private readonly IAdminUserRoleRepository _roles;
        private readonly IPasswordHasher _passwordHasher;

        public PostgresAdminAuthenticationProvider(
            IAdminLocalCredentialRepository credentials,
            IAdminUserRoleRepository roles,
            IPasswordHasher passwordHasher)
        {
            _credentials = credentials;
            _roles = roles;
            _passwordHasher = passwordHasher;
        }

        public async Task<Result<AdminAuthenticatedIdentity>> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Result.Failure<AdminAuthenticatedIdentity>(InvalidCredentials);

            var subject = username.Trim();

            var credentialResult = await _credentials.GetBySubjectAsync(subject, cancellationToken);
            if (credentialResult.IsFailure)
                return Result<AdminAuthenticatedIdentity>.Fail(credentialResult.Errors);

            var credential = credentialResult.Value;
            if (credential is null || !_passwordHasher.Verify(password, credential.PasswordHash))
                return Result.Failure<AdminAuthenticatedIdentity>(InvalidCredentials);

            var assignmentResult = await _roles.GetBySubjectAsync(subject, cancellationToken);
            if (assignmentResult.IsFailure)
                return Result<AdminAuthenticatedIdentity>.Fail(assignmentResult.Errors);

            var assignment = assignmentResult.Value;
            if (assignment is null)
                return Result.Failure<AdminAuthenticatedIdentity>(InvalidCredentials);

            var roles = assignment.Roles
                .Where(static role => !string.IsNullOrWhiteSpace(role))
                .Select(static role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Result.Success(new AdminAuthenticatedIdentity(
                assignment.Subject,
                assignment.DisplayName,
                roles));
        }
    }
}
