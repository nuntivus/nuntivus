# Admin API Authentication and RBAC

Kairo admin endpoints (`/v1/admin/*`) are protected by JWT authentication and role-based authorization.

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

Current admin read endpoints are protected with `admin.viewer`.

## Token requirements

Your access token should include:

- a valid issuer/audience matching `Authority` and `Audience`
- role claims containing one of the allowed roles for the endpoint policy

## Operational guidance

- Use your identity provider groups/app roles to issue `Viewer`/`Operator`/`Admin` roles.
- Follow least privilege: assign `Viewer` broadly, `Operator` to responders, `Admin` to platform administrators only.
- Rotate signing keys and enforce token lifetime policies in your identity provider.
