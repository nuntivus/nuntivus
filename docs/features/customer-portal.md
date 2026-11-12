# Customer self-service portal

The `/portal` dashboard route is separate from the administrative dashboard. It supports administrator-invitation-only access to read-only contracts and delivery history. There is no public signup, retry, requeue, destination editing, secret management or customer mutation endpoint.

## Identity and authorization

Local and Clerk providers implement `IPortalIdentityProvider`. Separate JWT bearer schemes validate local HS256 and Clerk RS256 tokens. Portal tokens do not authenticate administrative endpoints. Every portal list/object query joins the validated `(issuer, subject)` to internal active account memberships; caller-supplied customer and object IDs never grant access. Local credential versions are also checked in SQL. Revocation therefore applies to the next data request without waiting for token expiry. Foreign IDs return an empty page without revealing existence.

Response models deliberately omit event payloads, destination configuration, credentials, response bodies and raw failure text. Contracts expose IDs, enabled status and event types; deliveries expose IDs, contract/customer IDs, event type, status and attempt count. Lists are paged at 50 entries (pages 1–1000000). `id` can narrow contract/delivery lists to a single object, still with membership enforcement. This is an administrative-provider-neutral boundary, not a way to reuse admin roles as tenant membership.

## Invitations and recovery

Administrators use **Customer detail → Customer portal access**, or:

- `POST /v1/admin/portal/invitations`: `customerId`, `provider` (`local` or `clerk`), exact `subject`.
- `DELETE /v1/admin/portal/invitations/{id}`: invalidate an invitation.
- `GET /v1/admin/portal/customers/{customerId}/memberships?page=1`.
- `DELETE /v1/admin/portal/customers/{customerId}/memberships/{accountId}`: revoke membership and pending invitations for that account/customer.

The returned random 256-bit invitation token is shown once and must be shared privately. Only its SHA-256 hash is persisted; tokens and passwords are excluded from audit records. Invitations expire after two days and redemption is single-use under row locking. They are bearer capabilities: protect their distribution like a password-reset link. Subjects are exact, case-sensitive local usernames or Clerk user IDs, not inferred email addresses. No email delivery service is assumed.

Local users redeem with `POST /v1/portal/local/accept` (`token`, `password` of 12–256 characters). Tokens are entered into the portal form, not placed in URLs. Existing local account redemption resets the password and increments its credential version, invalidating prior sessions. Recovery requires a fresh administrator-issued invitation. Clerk users sign in first and redeem through `POST /v1/portal/invitations/accept`; the verified issuer and subject must exactly match the invitation. Clerk password/account recovery remains with Clerk.

`POST /v1/portal/local/login` accepts `username` and `password`. Local access tokens last 30 minutes. Login and redemption are throttled to ten requests per minute per remote IP per API process with no queue; configure trusted proxy forwarding and distributed perimeter throttling for multi-instance deployments. All membership/invitation mutations and redemption are audited atomically, without secret values. Account disabling is enforced by data queries; this version exposes membership revocation rather than a global account-disable UI.

## Local configuration

Portal providers default to disabled. Configure:

- `Portal:LocalEnabled=true`
- `Portal:LocalIssuer=nuntivus.portal.local`
- `Portal:LocalAudience=nuntivus.portal`
- `Portal:SigningKey`: independently generated base64 key of at least 32 bytes. Do not reuse the administrative signing key/audience.

Use ASP.NET configuration environment names such as `Portal__SigningKey`. The portal stores its local token in a separate sessionStorage entry, never the administrator session. Sign-out clears it. Passwords use the existing PBKDF2 hasher. Expired tokens require sign-in again; there is no local refresh token.

## Clerk configuration

Configure a Clerk instance and a JWT template named `nuntivus-portal` with the intended portal audience. The SDK handles hosted sign-in and periodically obtains fresh template tokens.

- `Portal:ClerkEnabled=true`
- `Portal:ClerkAuthority`: exact HTTPS Clerk issuer, matching `iss` (including trailing-slash semantics).
- `Portal:ClerkAudience`: required token audience.
- `Portal:ClerkAuthorizedParties`: nonempty list of allowed `azp` application origins.
- Dashboard build setting `VITE_CLERK_PUBLISHABLE_KEY`: the public Clerk instance key.

Signing keys are fetched through HTTPS OIDC discovery/JWKS; issuer, audience, signature, expiration and authorized party are verified. No token claim containing a customer ID is trusted. The browser SDK is dynamically loaded only for configured Clerk portal access. No Clerk secret key is sent to the browser or required for membership authorization. Administrators must obtain the correct Clerk user ID before inviting that identity.

## Persistence and limits

Migration `015_portal_memberships.sql` creates independent accounts, memberships and invitations. Customer deletion cascades memberships/invitations; accounts can remain for other customer memberships. No existing admin identities are automatically enrolled. Invitation/account data currently has no scheduled cleanup; expired invitations remain unusable. Audit retention is 90 days. Both providers can be enabled for testing/mixed environments, with distinct issuers.

Offline integration tests use a local RSA key to exercise Clerk signature/issuer/audience/party validation without accessing Clerk. A real Clerk tenant, deployed callback origins and production JWT-template configuration still require deployment verification.
