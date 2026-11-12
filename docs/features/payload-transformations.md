# Versioned payload transformations

Contract transformations are snapshotted onto each delivery. Changing a contract does not change already queued deliveries. The `version` selects the operation format; it is not an edit counter. A SHA-256 content `identity` identifies the configuration independently of the format version. Object keys are sorted before hashing so PostgreSQL JSONB key ordering does not change it. Arrays retain order.

## Version 1

Existing dot-path source/target field mappings retain their behavior. The output is a projected object containing mapped fields only. Missing sources fail rather than silently dropping data.

## Version 2

Configure `version: 2`, `fieldMappings: []` and an `expression` object. No scripts, network, filesystem, clocks, randomness or dynamically loaded functions are available.

Each expression has a string `op` and the following fields:

| Operation | Fields | Behavior |
| --- | --- | --- |
| `select` | `path`, optional `scope` | JSON pointer lookup; empty pointer selects the whole value. Scope defaults to `root`; `item` is the current array-map element. |
| `constant` | `value` | Any literal JSON value, including null. |
| `default` | `value`, `fallback` | Evaluate fallback only when value is missing. Explicit null is preserved. |
| `if` | `condition`, `then`, `else` | Condition must be boolean; only the selected branch is evaluated. |
| `map` | `source`, `value` | Source must be an array. Evaluate value once per element with item scope; root scope remains the original input. No implicit flattening. |
| `object` | `fields` | An object whose member values are expressions; produces a new object. |
| `array` | `args` | Build an array from expressions. |
| `concat` | `args` | Concatenate strings without coercion. |
| `add` | `args` | Add decimal-range numbers, failing on overflow. |
| `equals` | `args` | Exactly two required values; structural JSON equality. |
| `and` | `args` | Boolean conjunction; arguments are evaluated eagerly. |
| `not` | `value` | Boolean negation. |

Argument arrays contain 1–100 expressions (`equals` requires exactly two). Missing values cause failure except when handled by `default`. Missing is distinct from null, false, zero and an empty string. Nested maps use the nearest current item; root always refers to the original input. JSON pointers use `~0` for `~` and `~1` for `/`.

Limits: 1,000 expression nodes; depth 32; 10,000 evaluated steps; input, expression and intermediate/output JSON limited to 1,000,000 characters. These limits apply to all execution paths. Invalid operation names, unknown fields and malformed expressions are rejected on save. Data-dependent type errors are reported during preparation.

Example expression:

```json
{
  "op": "object",
  "fields": {
	"orderId": { "op": "select", "path": "/order/id" },
	"label": {
	  "op": "default",
	  "value": { "op": "select", "path": "/label" },
	  "fallback": { "op": "constant", "value": "unknown" }
	}
  }
}
```

## Output validation and preview

`IContractPayloadPreparer` is shared by actual delivery and contract test preview/send. It runs the saved transformation, then validates the resulting JSON against the contract's snapshotted schema version for that event type. It does not substitute today's active input schema. Missing output schema versions and invalid output prevent destination execution and become terminal configuration failures. Schema repository errors also prevent network execution.

Preview returns the prepared payload, transformation content identity and selected output schema version. Preview does not create events, deliveries or outbox entries and never invokes an executor. Explicit test-send is still a separate, audited operation.

The shared contract editor supports typed JSON expressions for version 2 and preserves version-1 visual field mappings. The existing contract test panel displays prepared output.

## Persistence

Apply `012_transformation_identity.sql`. New contract and delivery writes store content identities in explicit `transformation_identity` columns. Delivery list/detail queries return the stored identity; historical rows remain null because no identity was recorded at creation. Existing complete configuration snapshots remain authoritative for historical execution. Contract views also expose the identity calculated from their current saved configuration.
