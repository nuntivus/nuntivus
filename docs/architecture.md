# Concepts and architecture

## Purpose

Applications publish business events once. Nuntivus defines how each customer
consumes those events: routing, schema versions, filtering, transformation,
authentication, delivery, retries, ordering, and operational visibility.

For example, the same `orders.created` event can be delivered to one customer
through signed HTTP and another through Kafka, with different schemas and filters.
Producers do not need to embed each customer's delivery configuration.

Nuntivus is not a general-purpose ETL engine, an internal service mesh, or an
anything-to-anything integration platform. SDKs and a declarative configuration
CLI are not current release deliverables.

## Domain model

- **Source:** a managed ingestion endpoint or connector. Source authentication,
  normalization, and transport ownership are separate from customer routing.
- **Event:** an accepted business event with a source-scoped idempotency key,
  event type, timestamp, JSON object payload, and bounded metadata.
- **Event definition:** the event's schema history and active schema version.
  Validation records distinguish configured, unconfigured, and historical states.
- **Customer:** the consumer organization whose contracts and delivery history
  can be managed independently.
- **Contract:** the customer's subscriptions, linked Sources and Destinations,
  filters, schema targets, transformations, retry/rate/ordering policies, and SLOs.
- **Destination:** a shared delivery endpoint with type-specific configuration
  and managed credential references. Contracts link to destinations; delivery
  preparation records the configuration and transformation identity it used.
- **Delivery:** persisted execution state and attempts for a routed event.
  Operators can inspect, retry, cancel, or replay according to state and permissions.

See the feature guides for exact API contracts and supported configuration.

## Runtime boundaries

```text
HTTP Sources -> API ------> PostgreSQL <------ Pull Source workers
							  |
					   transactional outbox
							  |
						NATS JetStream
							  |
				contract matching and delivery workers
							  |
				  customer destinations + PostgreSQL
```

PostgreSQL is the source of truth for business state. NATS JetStream distributes
asynchronous work; it is not a replacement for persisted events and deliveries.
Memory is used for caching, not durability.

The API manages configuration, ingestion, authenticated operations, and queries.
The Worker hosts independent background services for ingestion, outbox publishing,
matching, delivery dispatch/scheduling/recovery, retention, SLOs, and alerts.
The React dashboard calls the API and is built/run separately.

## Event flow

1. Authenticate and normalize the incoming Source envelope.
2. Commit the event and its outbox record in one database transaction.
3. Publish asynchronous work and match active customer contracts.
4. Evaluate filters and prepare the appropriate schema/transformation.
5. Persist and dispatch deliveries to linked destinations.
6. Record successes, failures, attempts, and scheduled retries.
7. Expose delivery history, SLOs, and alerts to authorized operators.

External pull Sources acknowledge messages or advance progress only after durable
acceptance. See [Sources](sources.md) for connector-specific limits.

## Durability and idempotency

Processing is **at least once**. Consumers must tolerate duplicates: an external
send can succeed immediately before a process fails to persist its result.
Nuntivus does not promise exactly-once side effects at customer destinations.

- Ingestion through `POST /v1/sources/{id}/events` deduplicates by
  `(source_id, idempotency_key)` and returns the persisted event identity.
- Outbox publishing uses the outbox ID as JetStream `MsgId`. The broker's
  duplicate window limits deduplication; distinct outbox records stay distinct.
- Matching uses database uniqueness to prevent duplicate logical deliveries.
- Delivery execution uses optimistic state guards and attempt uniqueness.
- Retry/requeue operations are repeat-safe; event replay uses a caller-provided
  replay key to preserve one replay identity and its outbox record.

These guards reduce duplicate logical work; they cannot make a non-idempotent
external receiver exactly once. See [ordering guarantees](delivery-ordering-guarantees.md)
and [feature verification](features/product-verification.md) for limitations.

## Schema and operational lifecycle

Schema validation, compatibility analysis, and audited activation support event
version changes. Typed transformations prepare contract-specific payloads;
network-free dry runs and sampled impact analysis help operators review changes.
Samples are evidence, not an exhaustive proof of compatibility.

Per-contract SLOs and destination-aware alerting expose delivery failures.
The customer portal is invitation-only and read-only. CSV/JSON exports are
staff-only, permission-gated metadata exports, not unrestricted payload dumps.

## Project layout

| Project | Responsibility |
| --- | --- |
| `NuntivusServer.Api` | ASP.NET Core endpoints and authentication |
| `NuntivusServer.Worker` | Background services and worker coordination |
| `NuntivusServer.Application` | Use cases, interfaces, and orchestration |
| `NuntivusServer.Domain` | Domain models and state transitions |
| `NuntivusServer.Contracts` | API-facing contracts |
| `NuntivusServer.Common` | Shared primitives |
| `NuntivusServer.Infastructure` | PostgreSQL, transports, and provider integrations |
| `NuntivusServer.Dashboard` | React/TypeScript dashboard |
| `NuntivusServer.UnitTests`, `NuntivusServer.IntegrationTests` | Backend coverage |
| `migration` | Ordered database migrations |

Runtime technologies include .NET 8, Dapper/Npgsql, PostgreSQL, NATS JetStream,
OpenTelemetry, and Docker Compose. APIs and schemas may change before a stable release.
