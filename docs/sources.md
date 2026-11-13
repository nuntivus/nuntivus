# Managed Sources

A Source owns ingestion, not customer routing. Create/configure Sources through
`/v1/sources`; contracts link Sources to the events customers receive.
Editing happens on the Source detail page in the dashboard.

## HTTP ingestion

Create an `HTTP_API` Source with a configuration such as
`{"authentication":{"type":"none"}}` for trusted local development, or
`{"authentication":{"type":"token","secretRef":"secret.orders.ingest"}}`
to require a managed token. Send it in `X-Nuntivus-Source-Token`.
Do not put raw tokens in Source configuration or URLs.

Publish to `POST /v1/sources/{id}/events` with:

```json
{
  "idempotencyKey": "order-123-created",
  "type": "orders.created",
  "timestamp": "2026-01-01T00:00:00Z",
  "payload": { "orderId": "123" },
  "metadata": { "correlationId": "request-123" }
}
```

The payload must be a JSON object. Metadata accepts only string `traceId` and
`correlationId` fields. Source ID plus idempotency key identifies duplicates.
HTTP is push-based; it does not require a permanent Source worker.
The old direct event-publish and generic webhook routes are not supported.

## Pull connectors

The Worker supervises enabled Sources with PostgreSQL leases. Each message or
polled document contains one UTF-8 JSON event envelope. `type` and object `payload`
are required. Timestamp can default to receipt time. Use an immutable upstream
message identity for idempotency, never a delivery attempt or receipt handle.
Arbitrary field mapping is not a general-purpose ingestion feature.

| Source type | Main configuration |
| --- | --- |
| RabbitMQ | `uri` (`amqps://`), `queue`, optional `username`/`secretRef` |
| Kafka | `bootstrapServers`, `topic`, `groupId`, optional `username`/`secretRef` |
| SQS | `region`, `queueUrl`, role credentials or `accessKeyId`/`secretRef` |
| External NATS | `url` (`tls://`), `stream`, `subject`, `durable` |
| HTTP polling | `url` (`https://`), optional structured headers/body |
| Folder | `path` within the Worker-configured `Sources:FolderRoot` |
| S3 | `region`, `bucket`, `prefix`, scoped role or managed credentials |

Polling/folder/S3 intervals are 10–3600 seconds, defaulting to 30 seconds.
Use [connection tests](features/connection-tests.md) to understand what each
probe verifies and what it intentionally does not consume or prove.

## Acceptance and failure handling

Connectors must not acknowledge, delete, commit offsets, or advance cursors before
the event and outbox transaction commits, including deduplicated acceptance.
A crash between commit and acknowledgement can produce redelivery.

RabbitMQ uses manual ACKs; Kafka uses stable groups/offsets; SQS uses visibility
timeouts and delete-on-success; external NATS uses its own durable consumer.
Never reuse Nuntivus's internal JetStream stream/durable for an external Source.
Folder and S3 inputs are not deleted by default.

Invalid input is retained for operator action rather than silently discarded.
Monitor leases, worker errors, queue lag, and poison messages. Connector behavior
and limitations must be validated with each actual upstream service.

## Deployment boundaries

- Apply the complete migration directory before enabling Worker Sources.
- Restrict folder Sources to a trusted mounted root. Use filesystem permissions
  to prevent untrusted symlink replacement; do not expose arbitrary host paths.
- Scope S3 roles to `ListBucket`/`GetObject` for the configured prefix and SQS
  roles to the required queue operations.
- Require encrypted remote transports and managed credential references.
- Multiple replicas require fenced claims for poll/cursor/file workloads and
  transport-native ownership for broker workloads.
