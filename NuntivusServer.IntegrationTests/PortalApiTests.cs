using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NuntivusServer.Application.Portal;
using NuntivusServer.Contracts.Portal;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using NuntivusServer.Api.Security;
using System.Security.Cryptography;

namespace NuntivusServer.IntegrationTests
{
    [Collection(nameof(ContractsApiCollection))]
    public sealed class PortalApiTests(ContractsApiFixture fixture) : IAsyncLifetime
    {
        private WebApplicationFactory<Program> _app = null!;
        private HttpClient _admin = null!;
        private HttpClient _customer = null!;
        public async Task InitializeAsync()
        {
            await fixture.ResetAsync();
            _app = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Portal:LocalEnabled"] = "true", ["Portal:SigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Portal:ClerkEnabled"] = "true", ["Portal:ClerkAuthority"] = "https://clerk.example.test",
                ["Portal:ClerkAudience"] = "portal", ["Portal:ClerkAuthorizedParties:0"] = "https://portal.example.test"
            })));
            _admin = _app.CreateClient();
            _admin.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            _admin.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
            _customer = _app.CreateClient();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                TRUNCATE portal_accounts, portal_invitations CASCADE;
                INSERT INTO customers (id, name, created_at, updated_at) VALUES ('portal-a', 'A', now(), now()), ('portal-b', 'B', now(), now());
                INSERT INTO contracts (id, customer_id, enabled, destination_type, destination_config, created_at, updated_at)
                VALUES (@A, 'portal-a', true, 'Http', '{"secret":"never-expose"}', now(), now()), (@B, 'portal-b', true, 'Http', '{}', now(), now());
                """, new { A = ContractA, B = ContractB });
        }
        private static readonly Guid ContractA = Guid.Parse("0068056e-de48-494b-9a01-c9563523ebd1");
        private static readonly Guid ContractB = Guid.Parse("b2b65d56-f0be-42db-86b4-9c21d52a353f");
        public Task DisposeAsync() { _customer.Dispose(); _admin.Dispose(); _app.Dispose(); return Task.CompletedTask; }

        [Fact]
        public async Task Local_invitation_enforces_tenant_isolation_no_admin_access_and_immediate_revocation()
        {
            var invitation = await Invite("local", "alice");
            var accepted = await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "correct horse battery staple"));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var token = (await accepted.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
            _customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var own = await _customer.GetStringAsync("/v1/portal/customers/portal-a/contracts");
            Assert.Contains(ContractA.ToString(), own);
            Assert.DoesNotContain("never-expose", own);
            Assert.DoesNotContain("destinationConfig", own);
            var foreign = await _customer.GetStringAsync($"/v1/portal/customers/portal-b/contracts?id={ContractB}");
            Assert.DoesNotContain(ContractB.ToString(), foreign);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _customer.GetAsync("/v1/admin/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _customer.GetAsync("/v1/exports/contracts")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _customer.PostAsJsonAsync($"/v1/deliveries/{Guid.NewGuid()}/retry", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "correct horse battery staple"))).StatusCode);
            var memberships = await _admin.GetFromJsonAsync<MembershipPage>("/v1/admin/portal/customers/portal-a/memberships");
            Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/v1/admin/portal/customers/portal-a/memberships/{memberships!.Items.Single().AccountId}")).StatusCode);
            Assert.DoesNotContain(ContractA.ToString(), await _customer.GetStringAsync("/v1/portal/customers/portal-a/contracts"));
        }

        [Fact]
        public async Task Recovery_invalidates_previous_local_token_and_secrets_never_enter_audit()
        {
            var invitation = await Invite("local", "recovery-user");
            var response = await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "initial password value"));
            var oldToken = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
            var recovery = await Invite("local", "recovery-user");
            var recovered = await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(recovery.Token, "replacement password value"));
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            _customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
            Assert.DoesNotContain(ContractA.ToString(), await _customer.GetStringAsync("/v1/portal/customers/portal-a/contracts"));
            var badLogin = await _customer.PostAsJsonAsync("/v1/portal/local/login", new PortalLoginRequest("recovery-user", "initial password value"));
            Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            var audits = string.Join(" ", await connection.QueryAsync<string>("SELECT details::text FROM audit_logs WHERE category = 'portal'"));
            Assert.DoesNotContain(invitation.Token, audits);
            Assert.DoesNotContain(recovery.Token, audits);
            Assert.DoesNotContain("replacement password", audits);
        }

        [Fact]
        public async Task External_invitation_requires_exact_issuer_and_subject_and_cannot_use_local_enrollment()
        {
            var invitation = await Invite("clerk", "user_123");
            Assert.Equal(HttpStatusCode.Forbidden, (await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "a sufficiently long password"))).StatusCode);
            using var scope = _app.Services.CreateScope();
            var portal = scope.ServiceProvider.GetRequiredService<IPortalService>();
            Assert.True((await portal.AcceptAsync(invitation.Token, null, new PortalIdentity("https://other.example.test", "user_123"), CancellationToken.None)).IsFailure);
            Assert.True((await portal.AcceptAsync(invitation.Token, null, new PortalIdentity("https://clerk.example.test", "user_other"), CancellationToken.None)).IsFailure);
            var accepted = (await portal.AcceptAsync(invitation.Token, null, new PortalIdentity("https://clerk.example.test", "user_123"), CancellationToken.None)).EnsureValue();
            Assert.Equal("https://clerk.example.test", accepted.Issuer);
            Assert.Empty((await portal.ContractsAsync(new PortalIdentity("nuntivus.portal.local", "user_123", 1), "portal-a", 1, null, CancellationToken.None)).EnsureValue().Items);
            Assert.Single((await portal.ContractsAsync(new PortalIdentity("https://clerk.example.test", "user_123"), "portal-a", 1, null, CancellationToken.None)).EnsureValue().Items);
        }

        [Fact]
        public async Task Revoked_invitation_cannot_be_redeemed_and_operator_cannot_invite()
        {
            var invitation = await Invite("local", "revoked-user");
            Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/v1/admin/portal/invitations/{invitation.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "a sufficiently long password"))).StatusCode);
            _admin.DefaultRequestHeaders.Remove("X-Test-Role");
            _admin.DefaultRequestHeaders.Add("X-Test-Role", "Operator");
            Assert.Equal(HttpStatusCode.Forbidden, (await _admin.PostAsJsonAsync("/v1/admin/portal/invitations", new PortalInvitationRequest("portal-a", "local", "operator-user"))).StatusCode);
        }
        [Fact]
        public async Task Clerk_jwt_requires_valid_signature_issuer_audience_and_authorized_party()
        {
            using var rsa = RSA.Create(2048);
            var key = new RsaSecurityKey(rsa) { KeyId = "portal-test-key" };
            using var app = _app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(PortalAuthentication.ClerkScheme, options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = "https://clerk.example.test" };
                    options.Configuration.SigningKeys.Add(key);
                    options.TokenValidationParameters.IssuerSigningKey = key;
                })));
            using var client = app.CreateClient();
            string Token(string issuer, string audience, string party, DateTime expires) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer, Audience = audience, NotBefore = DateTime.UtcNow.AddHours(-2), Expires = expires,
                Claims = new Dictionary<string, object> { ["sub"] = "user_jwt", ["azp"] = party },
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            });
            var invitation = await Invite("clerk", "user_jwt");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("https://clerk.example.test", "portal", "https://portal.example.test", DateTime.UtcNow.AddMinutes(10)));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/portal/invitations/accept", new PortalAcceptRequest(invitation.Token))).StatusCode);
            Assert.Contains(ContractA.ToString(), await client.GetStringAsync("/v1/portal/customers/portal-a/contracts"));
            foreach (var token in new[]
            {
                Token("https://evil.example.test", "portal", "https://portal.example.test", DateTime.UtcNow.AddMinutes(10)),
                Token("https://clerk.example.test", "wrong-audience", "https://portal.example.test", DateTime.UtcNow.AddMinutes(10)),
                Token("https://clerk.example.test", "portal", "https://evil.example.test", DateTime.UtcNow.AddMinutes(10)),
                Token("https://clerk.example.test", "portal", "https://portal.example.test", DateTime.UtcNow.AddHours(-1))
            })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/portal/customers")).StatusCode);
            }
        }

        [Fact]
        public async Task Delivery_history_is_membership_scoped_and_excludes_payload_and_errors()
        {
            var invitation = await Invite("local", "delivery-reader");
            var accepted = await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "correct horse battery staple"));
            _customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await accepted.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
            var own = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("""
                INSERT INTO events (id, type, idempotency_key, source_id, occurred_at, received_at, payload)
                VALUES (@Own, 'orders.created', @Key, @Source, now(), now(), '{}');
                INSERT INTO deliveries (id, event_id, contract_id, customer_id, event_type, payload, destination_type, destination_config,
                    status, attempt_count, last_error, created_at, updated_at)
                VALUES (@Own, @Own, @A, 'portal-a', 'orders.created', '{"private":"sensitive-payload"}', 'Http', '{"secret":"private-config"}',
                    'Failed', 2, 'sensitive-error', now(), now()),
                    (@Foreign, @Own, @B, 'portal-b', 'orders.created', '{}', 'Http', '{}', 'Pending', 0, null, now(), now());
                """, new { Own = own, Foreign = foreign, A = ContractA, B = ContractB, Source = ContractsApiFixture.SourceId, Key = own.ToString() });
            var response = await _customer.GetStringAsync("/v1/portal/customers/portal-a/deliveries");
            Assert.Contains(own.ToString(), response);
            Assert.DoesNotContain(foreign.ToString(), response);
            Assert.DoesNotContain("sensitive", response);
            Assert.DoesNotContain("private-config", response);
            Assert.DoesNotContain(foreign.ToString(), await _customer.GetStringAsync($"/v1/portal/customers/portal-b/deliveries?id={foreign}"));
            Assert.DoesNotContain(foreign.ToString(), await _customer.GetStringAsync($"/v1/portal/customers/portal-a/deliveries?id={foreign}"));
        }

        [Fact]
        public async Task Invitation_redemption_is_single_use_under_concurrency_and_expiry_is_enforced()
        {
            var invitation = await Invite("local", "concurrent-reader");
            var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(invitation.Token, "correct horse battery staple"))));
            Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.BadRequest));
            var expired = await Invite("local", "expired-reader");
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.ExecuteAsync("UPDATE portal_invitations SET expires_at = now() - interval '1 second' WHERE id = @Id", new { expired.Id });
            Assert.Equal(HttpStatusCode.BadRequest, (await _customer.PostAsJsonAsync("/v1/portal/local/accept", new PortalAcceptRequest(expired.Token, "correct horse battery staple"))).StatusCode);
        }

        [Fact]
        public async Task Anonymous_login_is_rate_limited()
        {
            HttpResponseMessage? last = null;
            for (var index = 0; index < 11; index++) last = await _customer.PostAsJsonAsync("/v1/portal/local/login", new PortalLoginRequest("unknown", "wrong-password"));
            Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        }

        private async Task<PortalInvitationView> Invite(string provider, string subject)
        {
            var response = await _admin.PostAsJsonAsync("/v1/admin/portal/invitations", new PortalInvitationRequest("portal-a", provider, subject));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PortalInvitationView>())!;
        }
        private sealed record TokenResponse(string AccessToken);
        private sealed record MembershipPage(PortalMembershipView[] Items);
    }
}
