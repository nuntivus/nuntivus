using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Dapper;
using KairoServer.Api.Common;
using KairoServer.Api.Controllers.Customers;
using KairoServer.Contracts.Admin;
using KairoServer.Contracts.Settings;
using Npgsql;

namespace KairoServer.IntegrationTests
{
    [Collection(nameof(DeliveriesApiCollection))]
    public sealed class AuthApiTests : IAsyncLifetime
    {
        private readonly DeliveriesApiFixture _fixture;

        public AuthApiTests(DeliveriesApiFixture fixture)
        {
            _fixture = fixture;
        }

        public Task InitializeAsync() => _fixture.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Me_returns_current_principal_when_authenticated()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Operator-Id", "operator-auth");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");

            var response = await client.GetAsync("/v1/auth/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
            Assert.NotNull(body);
            Assert.Equal("operator-auth", body!.Subject);
            Assert.Equal("operator-auth", body.DisplayName);
            Assert.True(body.IsAuthenticated);
            Assert.Contains("Operator", body.Roles);
        }

        [Fact]
        public async Task Me_returns_unauthorized_when_not_authenticated()
        {
            using var client = _fixture.CreateClient();

            var response = await client.GetAsync("/v1/auth/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(error);
            Assert.Equal("unauthorized", error!.Code);
            Assert.Equal(401, error.Status);
            Assert.Equal("Unauthorized.", error.Title);
            Assert.True(error.Errors.Count > 0);
            Assert.Equal("unauthorized", error.Errors[0].Code);
        }

        [Fact]
        public async Task Admin_user_roles_replace_persists_assignment_for_admin()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
            client.DefaultRequestHeaders.Add("X-Operator-Id", "admin-actor");

            var request = new ReplaceAdminUserRolesRequest("Ops User", new[] { "Viewer", "Operator" });
            var response = await client.PutAsJsonAsync("/v1/admin/users/ops-user/roles", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AdminUserRoleAssignmentResponse>();
            Assert.NotNull(body);
            Assert.Equal("ops-user", body!.Subject);
            Assert.Contains("Viewer", body.Roles);
            Assert.Contains("Operator", body.Roles);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            var roleCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM admin_user_roles WHERE subject = @subject;",
                new { subject = "ops-user" });

            Assert.Equal(2L, roleCount);
        }

        [Fact]
        public async Task Admin_user_roles_replace_returns_forbidden_for_non_admin()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");

            var request = new ReplaceAdminUserRolesRequest("Ops User", new[] { "Viewer" });
            var response = await client.PutAsJsonAsync("/v1/admin/users/ops-user/roles", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Admin_user_delete_removes_assignment_for_admin()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

            var createRequest = new ReplaceAdminUserRolesRequest("Delete User", new[] { "Viewer" });
            var createResponse = await client.PutAsJsonAsync("/v1/admin/users/delete-user/roles", createRequest);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            var deleteResponse = await client.DeleteAsync("/v1/admin/users/delete-user");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var userCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM admin_users WHERE subject = @subject;",
                new { subject = "delete-user" });
            Assert.Equal(0L, userCount);
        }

        [Fact]
        public async Task Admin_user_delete_is_idempotent_for_admin()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

            var createRequest = new ReplaceAdminUserRolesRequest("Delete Twice User", new[] { "Viewer" });
            var createResponse = await client.PutAsJsonAsync("/v1/admin/users/delete-twice-user/roles", createRequest);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            var firstDelete = await client.DeleteAsync("/v1/admin/users/delete-twice-user");
            var secondDelete = await client.DeleteAsync("/v1/admin/users/delete-twice-user");

            Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        }

        [Fact]
        public async Task Instance_settings_update_and_get_require_admin_and_persist()
        {
            using var adminClient = _fixture.CreateClient();
            adminClient.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
            adminClient.DefaultRequestHeaders.Add("X-Operator-Id", "settings-admin");

            var updateRequest = new UpdateInstanceSettingsRequest(
                new InstanceDeliveryDefaultsSettingsView(45, 7, 350),
                new InstanceRetryDefaultsSettingsView(20, 1800, 2.5),
                new InstanceSecuritySettingsView(true, false, true),
                new InstanceRetentionSettingsView(120, 400, 400),
                new InstanceLimitsSettingsView(300000, 70000, 600, 8));

            var updateResponse = await adminClient.PutAsJsonAsync("/v1/admin/settings/instance", updateRequest);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var getResponse = await adminClient.GetAsync("/v1/admin/settings/instance");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var body = await getResponse.Content.ReadFromJsonAsync<InstanceSettingsView>();
            Assert.NotNull(body);
            Assert.Equal(45, body!.DeliveryDefaults.RequestTimeoutSeconds);
            Assert.Equal(7, body.DeliveryDefaults.MaxAttempts);
            Assert.Equal(2.5, body.RetryDefaults.BackoffMultiplier);
            Assert.True(body.Security.RequireHttpsDestinations);
            Assert.Equal(120, body.Retention.EventRetentionDays);
            Assert.Equal(300000, body.Limits.MaxEventPayloadBytes);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM instance_settings WHERE key = 'instance.delivery.defaults';");
            Assert.Equal(1L, count);

            var allKeysCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM instance_settings WHERE key IN ('instance.delivery.defaults','instance.retry.defaults','instance.security.defaults','instance.retention.defaults','instance.limits.defaults');");
            Assert.Equal(5L, allKeysCount);
        }

        [Fact]
        public async Task Instance_settings_contract_schema_is_stable()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

            var response = await client.GetAsync("/v1/admin/settings/instance");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var bodyStream = await response.Content.ReadAsStreamAsync();
            using var body = await JsonDocument.ParseAsync(bodyStream);

            Assert.True(body.RootElement.TryGetProperty("deliveryDefaults", out var deliveryDefaults));
            Assert.True(deliveryDefaults.TryGetProperty("requestTimeoutSeconds", out _));
            Assert.True(deliveryDefaults.TryGetProperty("maxAttempts", out _));
            Assert.True(deliveryDefaults.TryGetProperty("batchLimit", out _));

            Assert.True(body.RootElement.TryGetProperty("retryDefaults", out var retryDefaults));
            Assert.True(retryDefaults.TryGetProperty("initialDelaySeconds", out _));
            Assert.True(retryDefaults.TryGetProperty("maxDelaySeconds", out _));
            Assert.True(retryDefaults.TryGetProperty("backoffMultiplier", out _));

            Assert.True(body.RootElement.TryGetProperty("security", out var security));
            Assert.True(security.TryGetProperty("requireHttpsDestinations", out _));
            Assert.True(security.TryGetProperty("allowPrivateNetworkDestinations", out _));
            Assert.True(security.TryGetProperty("requireDestinationCredentialEncryption", out _));

            Assert.True(body.RootElement.TryGetProperty("retention", out var retention));
            Assert.True(retention.TryGetProperty("eventRetentionDays", out _));
            Assert.True(retention.TryGetProperty("deliveryAuditRetentionDays", out _));
            Assert.True(retention.TryGetProperty("operationAuditRetentionDays", out _));

            Assert.True(body.RootElement.TryGetProperty("limits", out var limits));
            Assert.True(limits.TryGetProperty("maxEventPayloadBytes", out _));
            Assert.True(limits.TryGetProperty("maxEventMetadataBytes", out _));
            Assert.True(limits.TryGetProperty("maxContractsPerCustomer", out _));
            Assert.True(limits.TryGetProperty("maxConcurrentReplayJobs", out _));

            Assert.True(body.RootElement.TryGetProperty("updatedAt", out _));
            Assert.True(body.RootElement.TryGetProperty("updatedBy", out _));
        }

        [Fact]
        public async Task Instance_settings_update_returns_forbidden_for_non_admin()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Operator");

            var request = new UpdateInstanceSettingsRequest(
                new InstanceDeliveryDefaultsSettingsView(30, 6, 500),
                new InstanceRetryDefaultsSettingsView(30, 3600, 2.0),
                new InstanceSecuritySettingsView(true, false, true),
                new InstanceRetentionSettingsView(90, 365, 365),
                new InstanceLimitsSettingsView(262144, 65536, 500, 5));

            var response = await client.PutAsJsonAsync("/v1/admin/settings/instance", request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Instance_settings_update_validates_limits_relationship()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

            var request = new UpdateInstanceSettingsRequest(
                new InstanceDeliveryDefaultsSettingsView(30, 6, 500),
                new InstanceRetryDefaultsSettingsView(30, 3600, 2.0),
                new InstanceSecuritySettingsView(true, false, true),
                new InstanceRetentionSettingsView(90, 365, 365),
                new InstanceLimitsSettingsView(1024, 2048, 500, 5));

            var response = await client.PutAsJsonAsync("/v1/admin/settings/instance", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task User_preferences_update_and_get_are_scoped_to_current_subject()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            client.DefaultRequestHeaders.Add("X-Operator-Id", "viewer-settings-user");

            var update = new UpdateUserPreferencesRequest("dark", 50, true);
            var updateResponse = await client.PutAsJsonAsync("/v1/settings/me/preferences", update);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var getResponse = await client.GetAsync("/v1/settings/me/preferences");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var body = await getResponse.Content.ReadFromJsonAsync<UserPreferencesSettingsView>();
            Assert.NotNull(body);
            Assert.Equal("dark", body!.Theme);
            Assert.Equal(50, body.DefaultPageSize);
            Assert.True(body.DenseTables);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM user_settings WHERE subject = @subject AND key = 'user.preferences';",
                new { subject = "viewer-settings-user" });
            Assert.Equal(1L, count);
        }

        [Fact]
        public async Task User_preferences_are_isolated_per_user()
        {
            using var firstUserClient = _fixture.CreateClient();
            firstUserClient.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            firstUserClient.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            firstUserClient.DefaultRequestHeaders.Add("X-Operator-Id", "prefs-user-a");

            using var secondUserClient = _fixture.CreateClient();
            secondUserClient.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            secondUserClient.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            secondUserClient.DefaultRequestHeaders.Add("X-Operator-Id", "prefs-user-b");

            var firstUpdate = await firstUserClient.PutAsJsonAsync(
                "/v1/settings/me/preferences",
                new UpdateUserPreferencesRequest("dark", 60, true));
            Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

            var secondGetBeforeUpdate = await secondUserClient.GetAsync("/v1/settings/me/preferences");
            Assert.Equal(HttpStatusCode.OK, secondGetBeforeUpdate.StatusCode);
            var secondBeforeBody = await secondGetBeforeUpdate.Content.ReadFromJsonAsync<UserPreferencesSettingsView>();
            Assert.NotNull(secondBeforeBody);
            Assert.Equal("system", secondBeforeBody!.Theme);
            Assert.Equal(20, secondBeforeBody.DefaultPageSize);
            Assert.False(secondBeforeBody.DenseTables);

            var secondUpdate = await secondUserClient.PutAsJsonAsync(
                "/v1/settings/me/preferences",
                new UpdateUserPreferencesRequest("light", 25, false));
            Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);

            var firstGetAfterSecondUpdate = await firstUserClient.GetAsync("/v1/settings/me/preferences");
            var secondGetAfterSecondUpdate = await secondUserClient.GetAsync("/v1/settings/me/preferences");

            var firstBody = await firstGetAfterSecondUpdate.Content.ReadFromJsonAsync<UserPreferencesSettingsView>();
            var secondBody = await secondGetAfterSecondUpdate.Content.ReadFromJsonAsync<UserPreferencesSettingsView>();

            Assert.NotNull(firstBody);
            Assert.NotNull(secondBody);
            Assert.Equal("dark", firstBody!.Theme);
            Assert.Equal(60, firstBody.DefaultPageSize);
            Assert.True(firstBody.DenseTables);
            Assert.Equal("light", secondBody!.Theme);
            Assert.Equal(25, secondBody.DefaultPageSize);
            Assert.False(secondBody.DenseTables);
        }

        [Fact]
        public async Task User_preferences_contract_schema_is_stable()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");

            var response = await client.GetAsync("/v1/settings/me/preferences");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var bodyStream = await response.Content.ReadAsStreamAsync();
            using var body = await JsonDocument.ParseAsync(bodyStream);

            Assert.True(body.RootElement.TryGetProperty("theme", out _));
            Assert.True(body.RootElement.TryGetProperty("defaultPageSize", out _));
            Assert.True(body.RootElement.TryGetProperty("denseTables", out _));
            Assert.True(body.RootElement.TryGetProperty("updatedAt", out _));
        }

        [Fact]
        public async Task User_preferences_update_validates_theme()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");

            var update = new UpdateUserPreferencesRequest("blue", 20, false);
            var response = await client.PutAsJsonAsync("/v1/settings/me/preferences", update);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task User_preferences_update_validates_theme_size_limit()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");

            var update = new UpdateUserPreferencesRequest(new string('d', 21), 20, false);
            var response = await client.PutAsJsonAsync("/v1/settings/me/preferences", update);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Diagnostics_endpoint_returns_operational_payload_for_authorized_user()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");
            client.DefaultRequestHeaders.Add(ApiErrorResponse.CorrelationIdHeader, "diag-correlation");

            var response = await client.GetAsync("/v1/ops/diagnostics");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.TryGetValues(ApiErrorResponse.CorrelationIdHeader, out var correlation));
            Assert.Equal("diag-correlation", correlation!.Single());

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.TryGetProperty("timestampUtc", out _));
            Assert.True(payload.RootElement.TryGetProperty("environment", out _));
            Assert.True(payload.RootElement.TryGetProperty("appVersion", out _));
            Assert.True(payload.RootElement.TryGetProperty("requestId", out _));
            Assert.True(payload.RootElement.TryGetProperty("correlationId", out var correlationId));
            Assert.True(payload.RootElement.TryGetProperty("readinessStatus", out _));
            Assert.Equal("diag-correlation", correlationId.GetString());
        }

        [Fact]
        public async Task Diagnostics_endpoint_returns_unauthorized_without_authentication()
        {
            using var client = _fixture.CreateClient();

            var response = await client.GetAsync("/v1/ops/diagnostics");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Viewer")]
        [InlineData("Operator")]
        public async Task Admin_user_role_assignment_prevents_privilege_escalation_for_non_admin(string role)
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", role);

            var request = new ReplaceAdminUserRolesRequest("Escalation Target", new[] { "Admin" });
            var response = await client.PutAsJsonAsync("/v1/admin/users/escalation-target/roles", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Protected_endpoint_returns_forbidden_for_insufficient_role()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Guest");

            var meResponse = await client.GetAsync("/v1/auth/me");
            var response = await client.GetAsync("/v1/admin/overview");

            Assert.Equal(HttpStatusCode.Forbidden, meResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var meError = await meResponse.Content.ReadFromJsonAsync<ApiErrorResponse>();
            Assert.NotNull(meError);
            Assert.Equal("forbidden", meError!.Code);
            Assert.Equal(403, meError.Status);
            Assert.Equal("Forbidden.", meError.Title);
            Assert.True(meError.Errors.Count > 0);
            Assert.Equal("forbidden", meError.Errors[0].Code);
        }

        [Fact]
        public async Task Protected_read_endpoint_returns_unauthorized_when_not_authenticated()
        {
            using var client = _fixture.CreateClient();

            var response = await client.GetAsync("/v1/customers");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Webhook_ingest_remains_anonymous()
        {
            using var client = _fixture.CreateClient();
            var payload = new
            {
                payload = JsonDocument.Parse("{}").RootElement,
            };

            var response = await client.PostAsJsonAsync("/v1/sources/webhooks/public-source", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Admin_users_me_returns_current_user_with_permissions()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Operator-Id", "viewer-user");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");

            var response = await client.GetAsync("/v1/admin/users/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AdminCurrentUserResponse>();
            Assert.NotNull(body);
            Assert.Equal("viewer-user", body!.Subject);
            Assert.Contains("Viewer", body.Roles);
            Assert.Contains("customers.read", body.Permissions);
            Assert.DoesNotContain("customers.write", body.Permissions);
        }

        [Fact]
        public async Task Admin_roles_returns_catalog_for_authorized_user()
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", "Viewer");

            var response = await client.GetAsync("/v1/admin/roles");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AdminRoleCatalogResponse>();
            Assert.NotNull(body);
            Assert.NotEmpty(body!.Roles);
            Assert.Contains(body.Roles, role => role.Name == "Viewer");
            Assert.Contains(body.Roles, role => role.Name == "Operator");
            Assert.Contains(body.Roles, role => role.Name == "Admin");
        }

        [Fact]
        public async Task Admin_roles_returns_unauthorized_when_not_authenticated()
        {
            using var client = _fixture.CreateClient();

            var response = await client.GetAsync("/v1/admin/roles");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Viewer", HttpStatusCode.OK)]
        [InlineData("Operator", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.OK)]
        public async Task Viewer_policy_endpoint_access_matrix(string role, HttpStatusCode expectedStatus)
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Operator-Id", $"matrix-viewer-{role.ToLowerInvariant()}");

            var response = await client.GetAsync("/v1/auth/me");

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Theory]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData("Operator", HttpStatusCode.Created)]
        [InlineData("Admin", HttpStatusCode.Created)]
        public async Task Operator_policy_endpoint_access_matrix(string role, HttpStatusCode expectedStatus)
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", role);

            var customerId = $"matrix-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..20];
            var response = await client.PostAsJsonAsync(
                "/v1/customers",
                new CreateCustomerRequest(customerId, $"Matrix {role}"));

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Theory]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData("Operator", HttpStatusCode.Forbidden)]
        [InlineData("Admin", HttpStatusCode.BadRequest)]
        public async Task Admin_policy_endpoint_access_matrix(string role, HttpStatusCode expectedStatus)
        {
            using var client = _fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
            client.DefaultRequestHeaders.Add("X-Test-Role", role);

            var request = new UpdateInstanceSettingsRequest(
                new InstanceDeliveryDefaultsSettingsView(30, 6, 500),
                new InstanceRetryDefaultsSettingsView(30, 3600, 2.0),
                new InstanceSecuritySettingsView(true, false, true),
                new InstanceRetentionSettingsView(90, 365, 365),
                new InstanceLimitsSettingsView(1024, 2048, 500, 5));
            var response = await client.PutAsJsonAsync("/v1/admin/settings/instance", request);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        private sealed record CurrentUserResponse(
            string Subject,
            string? DisplayName,
            IReadOnlyList<string> Roles,
            bool IsAuthenticated);

        private sealed record AdminCurrentUserResponse(
            string Subject,
            string? DisplayName,
            IReadOnlyList<string> Roles,
            IReadOnlyList<string> Permissions);

        private sealed record AdminRoleCatalogResponse(
            IReadOnlyList<AdminRoleResponse> Roles);

        private sealed record AdminRoleResponse(
            string Name,
            string Description,
            IReadOnlyList<string> Permissions);

        private sealed record AdminUserRoleAssignmentResponse(
            string Subject,
            string? DisplayName,
            IReadOnlyList<string> Roles,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt);
    }
}