using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Portal;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Contracts.Portal;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresPortalService(IDatabaseConnectionFactory connections, IEnumerable<IPortalIdentityProvider> providers,
        IPasswordHasher passwords, IAuditLogRepository audit, IUnitOfWork uow) : IPortalService
    {
        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        private static bool ValidPage(int page) => page is >= 1 and <= 1000000;
        private const string IdentityPredicate = "a.issuer = @Issuer AND a.subject = @Subject AND NOT a.disabled AND (@Version IS NULL OR a.credential_version = @Version)";
        private static object IdentityParameters(PortalIdentity identity, int page, string? customer = null, Guid? id = null) =>
            new { identity.Issuer, identity.Subject, Version = identity.CredentialVersion, Offset = (page - 1) * 50, Customer = customer, Id = id };

        public async Task<Result<PortalInvitationView>> InviteAsync(PortalInvitationRequest request, string actor, CancellationToken ct)
        {
            var provider = providers.SingleOrDefault(p => p.Name == request.Provider && p.Enabled);
            if (provider is null || string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Subject)
                || request.Subject.Length > 200 || request.Subject != request.Subject.Trim() || string.IsNullOrWhiteSpace(actor))
                return Result.Failure<PortalInvitationView>(Error.Validation("Provide an enabled provider, customer and exact subject of at most 200 characters."));
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var id = Guid.NewGuid();
            var expires = DateTimeOffset.UtcNow.AddDays(2);
            try
            {
                uow.Begin(true);
                var exists = await uow.GetConnection()!.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM customers WHERE id = @Id)", new { Id = request.CustomerId }, uow.GetTransaction(), cancellationToken: ct));
                if (!exists) { uow.Rollback(); return Result.Failure<PortalInvitationView>(Error.NotFound("Customer was not found.")); }
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO portal_invitations (id, customer_id, issuer, subject, token_hash, expires_at, created_at)
                    VALUES (@Id, @CustomerId, @Issuer, @Subject, @Hash, @Expires, now())
                    """, new { Id = id, request.CustomerId, provider.Issuer, request.Subject, Hash = Hash(token), Expires = expires }, uow.GetTransaction(), cancellationToken: ct));
                await Audit("invited", actor, request.CustomerId, new { Request = request, Response = new { Id = id, ExpiresAt = expires } }, ct);
                uow.Commit();
                return Result.Success(new PortalInvitationView(id, request.CustomerId, provider.Name, request.Subject, expires, token));
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<PortalInvitationView>(ex); }
        }

        public async Task<Result<PortalAccount>> AcceptAsync(string token, string? password, PortalIdentity? identity, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token) || token.Length != 64) return Result.Failure<PortalAccount>(Error.Validation("Invitation is invalid or expired."));
            try
            {
                uow.Begin(true);
                var invitation = await uow.GetConnection()!.QuerySingleOrDefaultAsync<InvitationRow>(new CommandDefinition("""
                    SELECT id AS Id, customer_id AS CustomerId, issuer AS Issuer, subject AS Subject FROM portal_invitations
                    WHERE token_hash = @Hash AND redeemed_at IS NULL AND revoked_at IS NULL AND expires_at > now() FOR UPDATE
                    """, new { Hash = Hash(token) }, uow.GetTransaction(), cancellationToken: ct));
                if (invitation is null) { uow.Rollback(); return Result.Failure<PortalAccount>(Error.Validation("Invitation is invalid or expired.")); }
                var provider = providers.SingleOrDefault(p => p.Enabled && p.Issuer == invitation.Issuer);
                if (provider is null || !provider.SupportsPassword && (identity is null || identity.Issuer != invitation.Issuer || identity.Subject != invitation.Subject)
                    || provider.SupportsPassword && (password is null || password.Length is < 12 or > 256))
                { uow.Rollback(); return Result.Failure<PortalAccount>(Error.Forbidden("Use the invited identity; local passwords must contain 12–256 characters.")); }
                var passwordHash = provider.SupportsPassword ? passwords.Hash(password!) : null;
                var account = await uow.GetConnection()!.QuerySingleAsync<PortalAccount>(new CommandDefinition("""
                    INSERT INTO portal_accounts (id, issuer, subject, password_hash, created_at)
                    VALUES (@Id, @Issuer, @Subject, @PasswordHash, now())
                    ON CONFLICT (issuer, subject) DO UPDATE SET password_hash = COALESCE(EXCLUDED.password_hash, portal_accounts.password_hash),
                        credential_version = portal_accounts.credential_version + CASE WHEN EXCLUDED.password_hash IS NOT NULL THEN 1 ELSE 0 END
                    RETURNING id AS Id, issuer AS Issuer, subject AS Subject, credential_version AS CredentialVersion
                    """, new { Id = Guid.NewGuid(), invitation.Issuer, invitation.Subject, PasswordHash = passwordHash }, uow.GetTransaction(), cancellationToken: ct));
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO portal_memberships (account_id, customer_id, created_at) VALUES (@Account, @Customer, now())
                    ON CONFLICT (account_id, customer_id) DO UPDATE SET revoked_at = NULL;
                    UPDATE portal_invitations SET redeemed_at = now() WHERE id = @Id;
                    UPDATE portal_invitations SET revoked_at = now() WHERE issuer = @Issuer AND subject = @Subject
                        AND id <> @Id AND redeemed_at IS NULL AND revoked_at IS NULL;
                    """, new { Account = account.Id, Customer = invitation.CustomerId, invitation.Id, invitation.Issuer, invitation.Subject }, uow.GetTransaction(), cancellationToken: ct));
                await Audit("invitation_accepted", account.Id.ToString(), invitation.CustomerId, new { InvitationId = invitation.Id, AccountId = account.Id }, ct);
                uow.Commit();
                return Result.Success(account);
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure<PortalAccount>(ex); }
        }

        public async Task<Result<PortalAccount>> LoginAsync(string username, string password, CancellationToken ct)
        {
            var provider = providers.SingleOrDefault(p => p.Enabled && p.SupportsPassword);
            if (provider is null || string.IsNullOrWhiteSpace(username) || username.Length > 200 || string.IsNullOrEmpty(password) || password.Length > 256)
                return Result.Failure<PortalAccount>(Error.Unauthorized("Invalid credentials."));
            try
            {
                using var connection = connections.CreateConnection();
                var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
                    SELECT id AS Id, issuer AS Issuer, subject AS Subject, password_hash AS PasswordHash, credential_version AS CredentialVersion
                    FROM portal_accounts a WHERE issuer = @Issuer AND subject = @Subject AND NOT disabled
                    AND EXISTS(SELECT 1 FROM portal_memberships m WHERE m.account_id = a.id AND m.revoked_at IS NULL)
                    """, new { provider.Issuer, Subject = username }, cancellationToken: ct));
                var valid = passwords.Verify(password, account?.PasswordHash ?? DummyHash);
                return account is null || !valid ? Result.Failure<PortalAccount>(Error.Unauthorized("Invalid credentials."))
                    : Result.Success(new PortalAccount(account.Id, account.Issuer, account.Subject, account.CredentialVersion));
            }
            catch (Exception ex) { return Result.Failure<PortalAccount>(ex); }
        }
        private static readonly string DummyHash = new NuntivusServer.Infastructure.Security.Pbkdf2PasswordHasher().Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

        public async Task<Result<PagedResult<PortalMembershipView>>> MembershipsAsync(string customerId, int page, CancellationToken ct)
        {
            if (!ValidPage(page)) return Result.Failure<PagedResult<PortalMembershipView>>(Error.Validation("Invalid page."));
            try
            {
                using var connection = connections.CreateConnection();
                using var rows = await connection.QueryMultipleAsync(new CommandDefinition("""
                    SELECT a.id AS AccountId, m.customer_id AS CustomerId, a.issuer AS Issuer, a.subject AS Subject,
                        (m.revoked_at IS NOT NULL) AS Revoked FROM portal_memberships m JOIN portal_accounts a ON a.id = m.account_id
                    WHERE m.customer_id = @Customer ORDER BY a.id LIMIT 50 OFFSET @Offset;
                    SELECT count(*) FROM portal_memberships WHERE customer_id = @Customer;
                    """, new { Customer = customerId, Offset = (page - 1) * 50 }, cancellationToken: ct));
                return Result.Success(PagedResult<PortalMembershipView>.Create(await rows.ReadAsync<PortalMembershipView>(), page, 50, await rows.ReadSingleAsync<long>()));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<PortalMembershipView>>(ex); }
        }

        public Task<Result> RevokeAsync(Guid accountId, string customerId, string actor, CancellationToken ct) => Revoke("""
            UPDATE portal_memberships SET revoked_at = COALESCE(revoked_at, now()) WHERE account_id = @Id AND customer_id = @Customer;
            UPDATE portal_invitations SET revoked_at = now() WHERE customer_id = @Customer AND redeemed_at IS NULL AND revoked_at IS NULL
                AND (issuer, subject) IN (SELECT issuer, subject FROM portal_accounts WHERE id = @Id);
            """, accountId, customerId, actor, "membership_revoked", ct);
        public Task<Result> RevokeInvitationAsync(Guid id, string actor, CancellationToken ct) => Revoke(
            "UPDATE portal_invitations SET revoked_at = COALESCE(revoked_at, now()) WHERE id = @Id", id, null, actor, "invitation_revoked", ct);
        private async Task<Result> Revoke(string sql, Guid id, string? customer, string actor, string action, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(actor)) return Result.Failure(Error.Unauthorized("Actor is required."));
            try
            {
                uow.Begin(true);
                await uow.GetConnection()!.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Customer = customer }, uow.GetTransaction(), cancellationToken: ct));
                await Audit(action, actor, customer, new { Id = id, CustomerId = customer }, ct);
                uow.Commit();
                return Result.Success();
            }
            catch (Exception ex) { uow.Rollback(); return Result.Failure(ex); }
        }

        public async Task<Result<PagedResult<PortalCustomerView>>> CustomersAsync(PortalIdentity identity, int page, CancellationToken ct)
        {
            var from = $"FROM customers c JOIN portal_memberships m ON m.customer_id = c.id JOIN portal_accounts a ON a.id = m.account_id WHERE {IdentityPredicate} AND m.revoked_at IS NULL";
            return await Query<PortalCustomerView>($"SELECT c.id AS Id, c.name AS Name {from} ORDER BY c.id LIMIT 50 OFFSET @Offset; SELECT count(*) {from}", IdentityParameters(identity, page), page, ct);
        }
        public Task<Result<PagedResult<PortalContractView>>> ContractsAsync(PortalIdentity identity, string customerId, int page, Guid? id, CancellationToken ct)
        {
            var from = $"FROM contracts c JOIN portal_memberships m ON m.customer_id = c.customer_id JOIN portal_accounts a ON a.id = m.account_id WHERE {IdentityPredicate} AND m.revoked_at IS NULL AND c.customer_id = @Customer AND (@Id IS NULL OR c.id = @Id)";
            return Query<PortalContractView>($"SELECT c.id AS Id, c.customer_id AS CustomerId, c.enabled AS Enabled, ARRAY(SELECT event_type FROM contract_event_types WHERE contract_id = c.id ORDER BY event_type) AS EventTypes {from} ORDER BY c.id LIMIT 50 OFFSET @Offset; SELECT count(*) {from}", IdentityParameters(identity, page, customerId, id), page, ct);
        }
        public Task<Result<PagedResult<PortalDeliveryView>>> DeliveriesAsync(PortalIdentity identity, string customerId, int page, Guid? id, CancellationToken ct)
        {
            var from = $"FROM deliveries d JOIN contracts c ON c.id = d.contract_id AND c.customer_id = d.customer_id JOIN portal_memberships m ON m.customer_id = d.customer_id JOIN portal_accounts a ON a.id = m.account_id WHERE {IdentityPredicate} AND m.revoked_at IS NULL AND d.customer_id = @Customer AND (@Id IS NULL OR d.id = @Id)";
            return Query<PortalDeliveryView>($"SELECT d.id AS Id, d.contract_id AS ContractId, d.customer_id AS CustomerId, d.event_type AS EventType, d.status AS Status, d.attempt_count AS AttemptCount {from} ORDER BY d.created_at DESC, d.id LIMIT 50 OFFSET @Offset; SELECT count(*) {from}", IdentityParameters(identity, page, customerId, id), page, ct);
        }
        private async Task<Result<PagedResult<T>>> Query<T>(string sql, object parameters, int page, CancellationToken ct)
        {
            if (!ValidPage(page)) return Result.Failure<PagedResult<T>>(Error.Validation("Invalid page."));
            try
            {
                using var connection = connections.CreateConnection();
                using var rows = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
                return Result.Success(PagedResult<T>.Create(await rows.ReadAsync<T>(), page, 50, await rows.ReadSingleAsync<long>()));
            }
            catch (Exception ex) { return Result.Failure<PagedResult<T>>(ex); }
        }
        private async Task Audit(string action, string actor, string? customer, object details, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            (await audit.AddAsync(AuditLogEntry.Create("portal", action, actor, customer, null, null, null, null, JsonSerializer.Serialize(details), now, now.AddDays(90)), uow, ct)).EnsureSuccess();
        }
        private sealed class InvitationRow
        {
            public Guid Id { get; set; }
            public string CustomerId { get; set; } = "";
            public string Issuer { get; set; } = "";
            public string Subject { get; set; } = "";
        }
        private sealed class AccountRow
        {
            public Guid Id { get; set; }
            public string Issuer { get; set; } = "";
            public string Subject { get; set; } = "";
            public string? PasswordHash { get; set; }
            public long CredentialVersion { get; set; }
        }
    }
}
