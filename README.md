<img width="2172" height="724" alt="Kairo Event" src="https://github.com/user-attachments/assets/4736401d-056f-4b5d-b536-bafac2f48e71" />

# ✈️ Kairo Server

**Self-hosted customer event infrastructure for SaaS and platform teams.**

> **Publish once. Define each customer's event contract. Kairo handles the rest.**

Kairo Server is the core backend of **Kairo Event**.

Kairo helps SaaS and platform teams define, deliver, evolve, and operate the events they expose to customers and external systems.

Applications publish business events once.

Kairo manages how each customer consumes them:

* which events they receive
* which schema and version they use
* how events are filtered or transformed
* which protocol and destination are used
* authentication and rate limits
* retries and ordering
* delivery reliability
* customer-specific SLOs
* schema migrations and deprecations
* operational impact when something changes or fails

Kairo is designed to become the **customer event control plane** between your applications and the systems operated by your customers and partners.

---

## Why Kairo?

Customer-facing event delivery often starts simple:

```text
Application
    │
    │ order.created
    ▼
Webhook
    │
    ▼
Customer
```

Then more customers arrive.

And every customer starts needing something different:

```text
ACME
→ HTTP
→ HMAC
→ schema v3
→ EU orders only

Globex
→ Kafka
→ schema v2
→ custom transformation

Umbrella
→ SQS
→ schema v3
→ 500 events/sec

Wayne
→ NATS
→ strict ordering
→ 99.99% delivery SLO
```

Production requirements quickly grow into:

```text
retries
rate limits
authentication
signing
schema versions
transformations
replay
delivery history
customer-specific rules
failure isolation
multiple protocols
schema migrations
deprecation tracking
SLO monitoring
impact analysis
```

At that point, the problem is no longer just:

> How do we send a webhook?

It becomes:

> How do we manage the event contract between our platform and every customer?

Kairo provides that infrastructure.

Your applications publish business events.

Kairo owns the customer-facing event lifecycle.

---

# Core Idea

Kairo separates the event produced by your application from the way each customer consumes it.

```text
                    INTERNAL SYSTEMS

                    Order Service
                         │
                         │ order.created
                         ▼
                    ┌─────────┐
                    │  Kairo  │
                    └────┬────┘
                         │
                  Canonical Event
                         │
                         ▼
                Customer Contracts
             ┌───────────┼───────────┐
             │           │           │
             ▼           ▼           ▼
           ACME        Globex      Umbrella
             │           │           │
          HTTP         Kafka        SQS
        schema v3    schema v2    schema v3
```

The producer does not need to know:

* who consumes the event
* which protocol they use
* which schema version they expect
* which transformation they require
* their retry policy
* their delivery SLO
* their rate limit
* their authentication method

Kairo owns that contract.

---

# Core Model

```text
Source
  │
  ▼
Event
  │
  ▼
Event Definition
  │
  ▼
Customer
  │
  ▼
Contract
  │
  ▼
Delivery
```

---

## Source

A **Source** represents where an event enters Kairo.

Examples:

```text
HTTP API
Application / SDK
Webhook integration
NATS
Kafka
RabbitMQ
SQS
```

Different sources can use different protocols.

Once accepted, incoming data is normalized into Kairo's internal event model.

Sources exist to bring business events into Kairo.

Kairo is not intended to become a general-purpose integration or anything-to-anything routing platform.

---

## Event

An **Event** represents an immutable occurrence in a system.

Examples:

```text
order.created
order.cancelled
invoice.paid
payment.completed
user.deleted
deployment.completed
```

An event typically contains:

```text
id
type
source
timestamp
metadata
payload
```

Example:

```json
{
  "id": "evt_01J...",
  "type": "order.created",
  "source": "orders",
  "timestamp": "2026-09-06T08:00:00Z",
  "payload": {
    "orderId": "ord_123",
    "customerId": "cus_456",
    "amount": 299.90,
    "currency": "EUR"
  }
}
```

Accepted events are immutable.

---

# Event Definitions and Schemas

An **Event Definition** describes the public contract of an event type.

For example:

```text
order.created

Owner:
Orders Team

Versions:
v1
v2
v3

Current:
v3

Deprecated:
v1
```

Each version can have its own schema.

```text
order.created
     │
     ├── v1
     ├── v2
     └── v3
```

Kairo can use these relationships to understand:

* which customers consume each version
* which contracts depend on a schema
* whether a proposed change is compatible
* which customers must migrate
* whether an old version can safely be retired

---

# Customer

A **Customer** represents the external organization, tenant, partner, or independent consumer receiving events.

Example:

```text
Customer
└── ACME Corporation
```

A customer may have multiple event contracts.

```text
ACME
│
├── Orders
│
├── Payments
│
└── Invoices
```

This customer-centric model allows Kairo to answer operational questions such as:

```text
What events does ACME consume?

Which schema versions does ACME still use?

Are ACME's destinations healthy?

Is ACME meeting its delivery SLO?

Which incidents currently affect ACME?
```

---

# Contract

A **Contract** defines how one customer consumes one or more event types.

This is the central concept in Kairo.

A contract may define:

```text
customer

event types

schema versions

filters

transformations

protocol

destination

authentication

rate limits

retry policy

ordering policy

delivery SLO

regional constraints

enabled state
```

Example:

```text
Customer:
ACME

Contract:
Orders

Events:
order.created
order.updated

Schema:
v3

Filter:
region == "EU"

Protocol:
HTTP

Destination:
https://api.acme.com/events

Authentication:
HMAC

Rate limit:
100 requests/second

Retry policy:
exponential, up to 48 hours

Ordering:
orderId

Delivery SLO:
99.9% within 30 seconds
```

Another customer can consume the same event completely differently:

```text
Customer:
Globex

Events:
order.created

Schema:
v2

Protocol:
Kafka

Transformation:
globex-orders-v4

Rate limit:
2000 events/second

Delivery SLO:
99.99% within 10 seconds
```

The application still publishes only:

```text
order.created
```

Kairo handles the difference.

---

# Delivery

A **Delivery** represents one concrete execution of a customer contract for one event.

```text
event evt_123
     │
     ├── delivery dlv_001 → ACME       ✓ delivered
     ├── delivery dlv_002 → Globex     ↻ retrying
     └── delivery dlv_003 → Umbrella   ✓ delivered
```

Each delivery may contain:

```text
customer
contract
event version
schema version
transformation version
destination
state
attempt history
retry schedule
latency
response information
error information
timestamps
```

A failure for one customer does not block another customer.

```text
ACME       HTTP 503 → retrying
Globex     Kafka    → delivered
Umbrella   SQS      → delivered
```

---

# Runtime Delivery

Kairo is still a reliable event delivery engine.

A customer contract is useful only if Kairo can execute it reliably.

The runtime is responsible for:

```text
matching events to contracts

creating deliveries

protocol-specific dispatch

rate limiting

tenant isolation

retry scheduling

dead-letter handling

ordering

backpressure

replay

delivery history

destination health
```

Supported delivery protocols are expected to include:

```text
HTTP
Kafka
NATS
RabbitMQ
SQS
```

with additional protocols added over time.

---

# Schema Lifecycle

Customer-facing events evolve.

Kairo should help teams evolve them without losing track of external consumers.

Example:

```text
order.created

v1 → deprecated
v2 → supported
v3 → current
v4 → proposed
```

Before publishing `v4`, Kairo can determine:

```text
Affected contracts: 48

Compatible:
31

Transformation required:
12

Potentially incompatible:
5
```

A platform team should be able to answer:

> Which customers will be affected if we change this event?

without manually searching documentation, source code, queues, or spreadsheets.

---

# Version Migration

Kairo tracks which customers still depend on older versions.

Example:

```text
order.created.v2

Active customers:
14

Monthly deliveries:
18.4M

Sunset:
2027-01-01
```

Migration progress:

```text
14 customers
     │
     ▼
9 customers
     │
     ▼
3 customers
     │
     ▼
0 customers
```

Only when all active contracts have migrated can the platform safely retire the old version.

---

# Impact Analysis

Kairo connects static configuration with runtime behavior.

If a schema, destination, broker, event definition, or policy changes, Kairo should be able to determine the affected customers.

Example:

```text
Kafka Cluster A
       │
       ├── Globex
       ├── Wayne
       └── Stark
```

If the cluster becomes unavailable:

```text
Incident

Infrastructure:
Kafka Cluster A

Affected customers:
3

Affected contracts:
7

Affected event types:
order.created
payment.completed
invoice.created

Delayed events:
124,320
```

This allows platform teams to understand **customer impact**, not just infrastructure health.

---

# Customer-Specific SLOs

Different customers may have different reliability requirements.

Example:

```text
ACME

Target:
99.9% of events delivered within 30 seconds

Actual:
99.97%

Status:
Healthy
```

Globex may have a stronger contract:

```text
Globex

Target:
99.99% within 10 seconds

Actual:
99.91%

Status:
SLO violated
```

Kairo can correlate SLO violations with:

```text
destination failures
broker problems
retries
throttling
backlogs
latency
schema problems
```

This turns delivery telemetry into customer-level operational information.

---

# Customer Operations

Instead of showing only infrastructure metrics, Kairo should provide a customer-centric operational view.

Example:

```text
ACME Corporation

Contracts            4
Destinations          3

Events last hour      54,321
Delivered             54,290
Pending               18
Failed                13

P95 latency           2.3s

Delivery SLO
99.94% / 99.90%       ✓

Destination health
Orders HTTP           degraded
Billing Kafka         healthy
```

When support receives:

> We didn't receive order `ord_123`.

Kairo should allow the operator to reconstruct exactly what happened.

```text
Event
evt_83923

Customer
ACME

Contract
Orders

Received
12:01:04.102

Attempt #1
HTTP 503

Attempt #2
HTTP 503

Attempt #3
HTTP 200

Delivered
12:01:10.883

Total latency
6.78 seconds
```

---

# Safe Replay

Kairo allows operators to replay historical events.

Replay can be scoped by:

```text
customer
contract
event type
event version
time range
delivery state
```

Example:

```text
Replay

Customer:
ACME

Event:
order.created

Range:
2026-09-05 12:00 → 13:00

Events:
12,480

Rate:
100 events/sec
```

Replay should preserve customer isolation and avoid overwhelming downstream systems.

---

# Customer Isolation

One unhealthy customer must not affect everyone else.

```text
ACME destination unavailable
        │
        ▼
ACME backlog grows
```

while:

```text
Globex      continues
Umbrella    continues
Wayne       continues
```

Kairo is designed around customer-aware:

```text
rate limiting
backpressure
retries
worker scheduling
delivery queues
health
```

---

# Example

A SaaS platform publishes:

```text
order.created
```

Kairo handles:

```text
                           Order Service
                                │
                                │
                                ▼
                        order.created
                                │
                                ▼
                         ┌────────────┐
                         │   Kairo    │
                         └──────┬─────┘
                                │
                         Canonical Event
                                │
              ┌─────────────────┼──────────────────┐
              │                 │                  │
              ▼                 ▼                  ▼

            ACME              Globex            Umbrella

         schema v3           schema v2          schema v3
         EU only             transform v4       amount > 1000
         HTTP                Kafka              SQS
         HMAC                SASL               IAM
         100 req/s           2000 msg/s         500 msg/s
         99.9% / 30s         99.99% / 10s       99% / 60s
```

The Order Service knows none of these customer-specific details.

That is Kairo's responsibility.

---

# What Problem Does Kairo Solve?

Without Kairo, SaaS and platform teams often accumulate:

```text
webhook workers

retry schedulers

queue consumers

delivery databases

customer-specific routing

signing logic

schema repositories

transformation code

migration spreadsheets

replay scripts

dead-letter handling

rate limiting

customer support tooling

SLO dashboards

incident analysis

customer-specific integration code
```

Over time, the company has accidentally built its own customer event platform.

Kairo centralizes that responsibility.

```text
Application
    │
    │ business event
    ▼
  Kairo
    │
    ├── contract → ACME
    ├── contract → Globex
    ├── contract → Umbrella
    └── contract → Partner
```

---

# Positioning

Kairo focuses on:

> **The contract between a platform's business events and the customers that consume them.**

Kairo combines:

```text
Event Catalog
+
Customer Contracts
+
Schema Lifecycle
+
Reliable Delivery
+
Customer SLOs
+
Impact Analysis
```

The goal is not simply to send events.

The goal is to allow platforms to safely operate and evolve customer-facing events over their entire lifecycle.

---

# What Kairo Is Not

Kairo is not intended to be:

* a Kafka replacement
* a general-purpose message broker
* a workflow engine
* an ESB
* an iPaaS
* a generic automation platform
* a generic API gateway
* an arbitrary anything-to-anything integration engine

Kairo focuses specifically on the boundary between:

```text
your platform
```

and

```text
customers / partners / external consumers
```

---

# Architecture

```text
                         Sources
                            │
                            ▼
                   ┌─────────────────┐
                   │    Kairo API    │
                   │   ASP.NET Core  │
                   └────────┬────────┘
                            │
                            ▼
                   ┌─────────────────┐
                   │   PostgreSQL    │
                   │ source of truth │
                   └────────┬────────┘
                            │
                     Transactional
                         Outbox
                            │
                            ▼
                   ┌─────────────────┐
                   │ NATS JetStream  │
                   │ work transport  │
                   └────────┬────────┘
                            │
              ┌─────────────┼─────────────┐
              │             │             │
              ▼             ▼             ▼
          Contract       Delivery       Retry
           Matcher        Worker      Scheduler
              │             │             │
              └─────────────┼─────────────┘
                            │
                            ▼
                       PostgreSQL
```

Additional application services can build on this runtime:

```text
Schema Compatibility
Impact Analysis
SLO Aggregation
Customer Health
Lifecycle Management
Replay
```

The responsibilities remain intentionally separated:

```text
PostgreSQL
= durable business state

NATS JetStream
= asynchronous work distribution

Memory
= caching and hot-path performance
```

PostgreSQL remains the source of truth.

JetStream distributes asynchronous work between workers.

---

# Event Flow

```text
Source
  │
  ▼
Accept event
  │
  ▼
Persist event + outbox
  │
  ▼
Commit
  │
  ▼
Publish work
  │
  ▼
Resolve event definition
  │
  ▼
Match active customer contracts
  │
  ▼
Evaluate filters and policies
  │
  ▼
Resolve schema / transformation
  │
  ▼
Create deliveries
  │
  ▼
Dispatch
  │
  ├── success
  │      │
  │      ▼
  │   delivered
  │
  └── failure
         │
         ▼
       retry
         │
         ├── success → delivered
         │
         └── exhausted
                │
                ▼
               dead
```

Delivery telemetry then feeds:

```text
customer health
SLO calculations
impact analysis
delivery history
operational dashboards
```

---

# Delivery Guarantees

Kairo uses **at-least-once processing**.

An accepted event should not disappear because a:

```text
worker
broker
network connection
destination
database connection
```

temporarily fails.

Consumers should therefore be designed with idempotency in mind.

---

# Technology

```text
Runtime       .NET 8
API           ASP.NET Core
Database      PostgreSQL
Data access   Dapper + Npgsql
Messaging     NATS JetStream
Telemetry     OpenTelemetry
Deployment    Docker / Docker Compose
```

The Kairo dashboard is maintained separately from the server runtime.

---

# Self-Hosting

Kairo is designed to be self-hosted from the beginning.

A minimal deployment consists of:

```text
Kairo API
Kairo Worker
PostgreSQL
NATS JetStream
```

and should remain deployable with:

```bash
docker compose up -d
```

Production operations guides:

- [Self-hosting operations index](docs/self-hosting/README.md)
- [Production configuration guide](docs/self-hosting/production-configuration.md)
- [Backup guide](docs/self-hosting/backup.md)
- [Restore guide](docs/self-hosting/restore.md)
- [Upgrade guide](docs/self-hosting/upgrade.md)
- [Deployment examples](docs/self-hosting/deployment-examples.md)

Small installations can run with a minimal number of processes.

Larger installations can independently scale:

```text
API instances
matching workers
delivery workers
retry workers
SLO processors
```

without changing the Kairo programming model.

---

# Security Configuration Guidance

Use external secret stores or platform secret injection for all runtime credentials.
Do not commit production secrets to `appsettings*.json`.

## Destination credential encryption key

Kairo requires `DestinationCredentials:EncryptionKey` (base64, exactly 32 bytes) for protected destination credentials.

This setting is validated at startup for API and Worker.

- Prefer environment variable `KAIRO_DESTINATION_CREDENTIALS_KEY` in production.
- Rotate this key through your secret manager and rollout process.
- Keep non-production keys isolated per environment.

## Database credentials

- Prefer managed identity / workload identity where supported.
- If username/password is required, inject via secret manager and environment-specific configuration.
- Use least-privilege Postgres roles (separate migration/admin and runtime users).
- Enable TLS and certificate validation for remote Postgres connections.

## NATS credentials

- Prefer `Nats:CredsFile` (JWT/NKey) over static token or user/password.
- Configure exactly one auth mode: token, user/password, or creds file.
- Store credentials outside the container image and mount/inject at runtime.
- Rotate NATS credentials regularly and scope account permissions to required subjects only.

## Configuration model and startup validation

Kairo API and Worker use typed configuration sections (`Database`, `Nats`, `Worker`, `Delivery`, `DeliveryDispatcher`, `DeliveryOrdering`, `RetentionCleanup`, `EndpointSecurity`, `DestinationCredentials`, `Cache`, `HostOptions`) and validate them during startup.

Supported `KAIRO_*` env mappings include:

- `KAIRO_DATABASE_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `KAIRO_POSTGRES_CONNECTION_STRING` -> `Database:PostgresConnectionString`
- `KAIRO_DESTINATION_CREDENTIALS_KEY` -> `DestinationCredentials:EncryptionKey`
- `KAIRO_NATS_URL` -> `Nats:Url`
- `KAIRO_NATS_STREAM` -> `Nats:Stream`
- `KAIRO_NATS_SUBJECTS` -> `Nats:Subjects`
- `KAIRO_OTLP_ENDPOINT` -> `OpenTelemetry:OtlpEndpoint`
- `KAIRO_SHUTDOWN_TIMEOUT_SECONDS` -> `HostOptions:ShutdownTimeout`

---

# Infrastructure as Code

Customer contracts and event definitions should eventually be manageable through configuration and GitOps workflows.

Example:

```yaml
customer: acme

contract:
  name: orders

  events:
    - order.created
    - order.updated

  schema:
    order.created: v3

  filter:
    region: EU

  destination:
    type: http
    url: ${ACME_WEBHOOK_URL}

  authentication:
    type: hmac
    secret: ${ACME_SECRET}

  rateLimit:
    requestsPerSecond: 100

  retry:
    maxDuration: 48h

  slo:
    target: 99.9
    latency: 30s
```

Future CLI workflows may include:

```bash
kairo validate
kairo diff
kairo apply
```

The goal is to make customer event contracts reviewable, versioned, and reproducible.

---

# Project Structure

```text
kairo-server/
│
├── src/
│   ├── Kairo.Api/
│   ├── Kairo.Worker/
│   ├── Kairo.Application/
│   ├── Kairo.Domain/
│   ├── Kairo.Infrastructure/
│   └── Kairo.Contracts/
│
├── tests/
│
├── deploy/
│
├── docker-compose.yml
│
└── README.md
```

The backend follows a modular architecture with vertical application slices and clear separation between domain, application, infrastructure, and transport concerns.

---

# Related Projects

The Kairo Event ecosystem may include separate repositories for:

```text
kairo-server       Core control plane and delivery runtime
kairo-dashboard    Web administration interface
kairo-sdk-dotnet   .NET SDK
kairo-sdk-js       JavaScript / TypeScript SDK
kairo-cli          Command-line and GitOps tooling
kairo-docs         Documentation
```

---

# Vision

Kairo aims to become the infrastructure layer SaaS and platform companies use to manage customer-facing events.

Instead of every application knowing:

```text
which customer
which protocol
which schema
which endpoint
which retry policy
which transformation
which SLO
```

applications publish business events once.

Kairo manages the external contract.

```text
Publish
   ↓
Contract
   ↓
Deliver
   ↓
Observe
   ↓
Evolve
```

> **Define how every customer consumes your events. Kairo handles delivery, reliability, and evolution.**

---

# Idempotency Guarantees

Kairo uses **at-least-once processing** across pipeline stages and enforces deterministic outcomes for duplicates.

## Ingestion (`POST /events`)

- Idempotency key scope: `(source_id, idempotency_key)`
- Database guard: `events.uq_events_source_idempotency`
- Duplicate behavior: returns the already persisted `eventId` and does not create a second outbox row.

## Outbox -> JetStream

- Publish dedup key: outbox message id (`outbox_messages.id`) passed as JetStream `MsgId`
- Stream guard: JetStream duplicate window (`NatsOption.DuplicateWindow`)
- Duplicate behavior:
  - retrying the same outbox row is deduped by JetStream
  - different outbox rows for the same event remain distinct and are both published

## Contract Matcher

- Delivery uniqueness: `deliveries (event_id, contract_id)`
- Duplicate behavior: duplicate `event.accepted` processing inserts `0` deliveries and emits `0` new `delivery.ready` outbox rows.

## Delivery Execution

- Concurrency/idempotency guards:
  - optimistic write guard on `deliveries.updated_at`
  - unique delivery attempt key `(delivery_id, attempt_number)`
  - terminal-state short-circuiting (`Delivered`, `Dead`, non-retryable `Failed`)

## Retry / Manual Retry / Replay

- Automatic retry: safe under duplicate redelivery via the same delivery-row and attempt guards.
- Manual retry/requeue: repeat-safe operations; repeated requests converge to the same state.
- Replay: keyed by `(source_event_id, replay_key)` and returns the original replayed event for duplicate replay requests.

These guarantees provide repeat-safe behavior under retries, redeliveries, and operator replays without requiring exactly-once transport semantics.

---

# Status

> [!WARNING]
> Kairo is currently in early development and is **not yet production ready**.

APIs, schemas, configuration, domain concepts, and architecture may change before the first stable release.

---

# License

Kairo Event is source-available and self-hostable.

See [`LICENSE`](LICENSE) for the terms that apply to the current version.
