# Copilot Instructions

## Project Guidelines
- User prefers factory abstractions to use interfaces (e.g., IDeliveryFactory) instead of concrete-only factory usage.
- User prefers important contract snapshot fields to be explicitly persisted/queried as first-class columns instead of derived from JSON CASE expressions in SQL queries.
- User prefers destination-agnostic rotation logic; avoid HTTP-specific logic in shared service layer because additional destination types are expected.
- Nuntivus server is not deployed; for this project, do not preserve legacy API backward compatibility when implementing the Source ingestion architecture.
- Source detail pages should have a shared, source-type-agnostic overview and type-specific sections, because additional Source types will be added beyond HTTP.
- On the contract edit page, do not include the legacy inline Destination type/configuration form below the Destinations table, and avoid exposed snapshot terminology/controls in the destination table to reduce confusion.

## Delivery Operations
- For manual delivery operations, cancel applies to Pending/Failed/Dead states.
- Duplicate operations should return 200 with the current state.
- Audit should capture the full request/response snapshot.
- Operator identity should come from the authenticated user principal.

## Frontend Forms
- User wants reusable shared infrastructure across all existing forms.
- Implement route-change-only unsaved-change prompts.
- Use a shared modal dialog system consistently instead of ad-hoc confirms.
- On the contract edit page, show validation feedback only after Save is attempted, and keep the Destinations table visible even when no destinations are selected.

## Contract Management
- Contract edit Sources and Destinations should use matching table styles, remain visible when empty, and use a remove icon action rather than selection checkboxes for removing linked items.