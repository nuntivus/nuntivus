# Copilot Instructions

## Project Guidelines
- User prefers factory abstractions to use interfaces (e.g., IDeliveryFactory) instead of concrete-only factory usage.
- User prefers important contract snapshot fields to be explicitly persisted/queried as first-class columns instead of derived from JSON CASE expressions in SQL queries.
- User prefers destination-agnostic rotation logic; avoid HTTP-specific logic in shared service layer because additional destination types are expected.