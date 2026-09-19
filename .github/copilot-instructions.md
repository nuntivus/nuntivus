# Copilot Instructions

## Project Guidelines
- User prefers factory abstractions to use interfaces (e.g., IDeliveryFactory) instead of concrete-only factory usage.
- User prefers important contract snapshot fields to be explicitly persisted/queried as first-class columns instead of derived from JSON CASE expressions in SQL queries.
- User prefers destination-agnostic rotation logic; avoid HTTP-specific logic in shared service layer because additional destination types are expected.

## Delivery Operations
- For manual delivery operations, cancel applies to Pending/Failed/Dead states.
- Duplicate operations should return 200 with the current state.
- Audit should capture the full request/response snapshot.
- Operator identity should come from the authenticated user principal.