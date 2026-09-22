# Admin API Authentication and RBAC

Kairo admin endpoints (`/v1/admin/*`) are protected by JWT authentication and role-based authorization.

## Authentication architecture decision

Kairo uses external identity-provider issued access tokens (OIDC/OAuth2) validated by the API with JWT bearer authentication.

- Kairo does not implement local username/password login.
- Session lifetime, re-authentication, and token issuance are owned by the identity provider.
- Frontend authorization checks are UX-only; backend authorization remains the security boundary.

## Required configuration

Configure these API settings:

- `Authentication:Authority`
- `Authentication:Audience`

Using environment variables:

- `Authentication__Authority`
- `Authentication__Audience`

Example:

```bash
Authentication__Authority=https://login.example.com/realms/kairo
Authentication__Audience=kairo-api
```

## Roles and policies

Kairo defines these admin roles:

- `Viewer`
- `Operator`
- `Admin`

And these policies:

- `admin.viewer` -> allows `Viewer`, `Operator`, `Admin`
- `admin.operator` -> allows `Operator`, `Admin`
- `admin.admin` -> allows `Admin`

Admin read endpoints and dashboard-facing operational reads are protected with `admin.viewer`.

## Token requirements

Your access token should include:

- a valid issuer/audience matching `Authority` and `Audience`
- role claims containing one of the allowed roles for the endpoint policy

Expected claim contract:

- `sub`: stable principal identifier (used as the canonical subject)
- `name`: display name for UX
- `roles` (or `role`): admin role values (`Viewer`, `Operator`, `Admin`)

Issuer/audience behavior:

- `Authentication:Authority` is treated as the expected token issuer authority for signature and issuer validation.
- `Authentication:Audience` is treated as the required API audience (`aud`) value.
- Tokens that fail issuer/audience validation are rejected with `401 Unauthorized`.

## Frontend bootstrap auth contract

Kairo exposes `GET /v1/auth/me` for dashboard session bootstrap.
Kairo also exposes admin-scoped RBAC bootstrap endpoints:

- `GET /v1/admin/users/me`
- `GET /v1/admin/roles`

And admin-only management endpoints:

- `PUT /v1/admin/users/{subject}/roles`
- `DELETE /v1/admin/users/{subject}`
- `GET /v1/admin/settings/instance`
- `PUT /v1/admin/settings/instance`

User-scoped preference endpoints:

- `GET /v1/settings/me/preferences`
- `PUT /v1/settings/me/preferences`

- Requires an authenticated bearer token with at least `Viewer` role (`admin.viewer` policy).
- Returns `401` when the token is missing/invalid/expired.
- Returns `403` when authenticated but missing dashboard role.
- Returns `200` with the principal snapshot when valid and authorized.

Response shape:

- `subject` (string): stable principal identifier (`nameidentifier` claim when available)
- `displayName` (string, nullable): display label (`name` claim fallback)
- `roles` (string array): effective role claims used for UX gating
- `isAuthenticated` (bool): always `true` for successful calls

Admin user response (`GET /v1/admin/users/me`) includes:

- `subject`
- `displayName`
- `roles` (normalized known admin roles)
- `permissions` (derived permission strings for UX gating)

Role catalog response (`GET /v1/admin/roles`) includes role metadata:

- role `name`
- role `description`
- role `permissions`

Admin management write endpoints require `Admin` role (`admin.admin` policy).
Instance settings endpoints require `Admin` role (`admin.admin` policy).
User preference endpoints require at least `Viewer` role (`admin.viewer` policy) and are scoped to the authenticated principal.

Instance settings payload categories include:

- `deliveryDefaults`
- `retryDefaults`
- `security`
- `retention`
- `limits`

For protected endpoints:

- `401` means unauthenticated (frontend should clear session and trigger re-auth)
- `403` means authenticated but insufficient role (frontend should show unauthorized UX)

## Operational guidance

- Use your identity provider groups/app roles to issue `Viewer`/`Operator`/`Admin` roles.
- Follow least privilege: assign `Viewer` broadly, `Operator` to responders, `Admin` to platform administrators only.
- Rotate signing keys and enforce token lifetime policies in your identity provider.
