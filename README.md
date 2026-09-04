<img width="1774" height="887" alt="Kairo Event" src="https://github.com/user-attachments/assets/1f65413d-cd2b-4b03-adca-9ad09ca1d5c8" />

**Self-hosted event delivery infrastructure for SaaS and platform teams.**

> **Publish once. Deliver reliably. Own the infrastructure.**

Kairo Event helps applications reliably deliver business events to customers, partners, APIs, queues, and event brokers without building and maintaining custom delivery infrastructure.

Your application publishes an event once. Kairo handles subscriptions, routing, delivery, retries, rate limits, signing, replay, and delivery history for every subscriber.

```text
                         Kairo Event

Your Application
      │
      │ order.created
      ▼
┌─────────────────────────────────────────────┐
│                                             │
│                    KAIRO                    │
│                                             │
│  persist → match subscriptions → deliver   │
│                ↓                            │
│      retry / rate limit / replay            │
│                                             │
└───────────────┬───────────────┬─────────────┘
                │               │
                ▼               ▼
          ACME Webhook      Globex Kafka
                │
                ▼
          Partner API
```

## Why Kairo?

A webhook sender often starts simple:

```text
event
  ↓
HTTP POST
  ↓
customer
```

Then production requirements appear:

- What happens when the customer endpoint is offline?
- How long should deliveries be retried?
- How do we prevent duplicate deliveries?
- How do we sign requests?
- How do we enforce customer-specific rate limits?
- How do we see every delivery attempt?
- How do we replay failed events?
- How do we support customers that want Kafka or SQS instead of HTTP?
- How do we prevent one broken destination from affecting everybody else?

Eventually the application contains its own:

```text
webhook worker
retry scheduler
dead-letter handling
signing logic
rate limiting
delivery database
replay scripts
endpoint configuration
monitoring
queue integrations
```

At that point, the team is building an event delivery platform.

**Kairo is that platform.**

Your application should only need to say:

```text
order.created happened
```

Kairo owns how every subscriber receives it.

---

## Core Model

Kairo is built around five main concepts:

```text
Source
  ↓
Event
  ↓
Subscription
  ↓
Destination
  ↓
Delivery
```

### Source

A **Source** identifies where events enter Kairo.

Examples:

- Application API
- Kairo SDK
- NATS
- Kafka

### Event

An **Event** represents something that happened in your application.

Examples:

```text
order.created
invoice.paid
user.deleted
deployment.completed
```

Events are immutable once accepted.

### Subscription

A **Subscription** defines who wants an event and the rules under which it should be delivered.

A subscription can define:

- event type
- subscriber
- destination
- filters
- transformations
- authentication
- retry policy
- rate limits

Example:

```text
Subscriber:     ACME
Event:          order.created
Destination:    ACME Production Webhook
Filter:         region == "EU"
Rate limit:     10 requests/second
Retry policy:   exponential, up to 48 hours
```

### Destination

A **Destination** defines where and how Kairo delivers events.

Planned destination types include:

- HTTP / Webhooks
- Kafka
- NATS
- AWS SQS
- RabbitMQ

A destination stores transport-specific configuration such as endpoint, credentials, authentication, timeout, and concurrency limits.

### Delivery

A **Delivery** represents one event being delivered through one subscription to one destination.

```text
event evt_123
     │
     ├── delivery dlv_001 → ACME      ✓ delivered
     ├── delivery dlv_002 → Globex    ↻ retrying
     └── delivery dlv_003 → Partner   ✓ delivered
```

Each delivery keeps its own attempt history.

---

## Example

Imagine a SaaS application publishes:

```text
invoice.paid
```

Three customers consume the event differently:

```text
                         invoice.paid
                              │
                              ▼
                            Kairo
                              │
            ┌─────────────────┼─────────────────┐
            ▼                 ▼                 ▼

          ACME             Globex           Umbrella

       HTTP webhook         Kafka              SQS
       HMAC signing      transformation      EU only
       10 req/sec
       retry 48h
```

The application does not need customer-specific delivery code.

If ACME is unavailable:

```text
ACME       503 → retry
Globex     delivered
Umbrella   delivered
```

Only ACME's delivery is affected.

Kairo records each attempt and retries according to the subscription policy.

---

## What Kairo Solves

### Reliable external event delivery

External systems are unreliable.

Kairo handles:

- transient failures
- timeouts
- HTTP `429`
- HTTP `5xx`
- exponential backoff
- jitter
- dead deliveries
- replay

### Producer / consumer decoupling

Without Kairo:

```text
Order Service
 ├── knows ACME webhook
 ├── knows Globex Kafka
 ├── knows Partner API
 └── knows Analytics
```

With Kairo:

```text
Order Service
      │
      ▼
order.created
      │
      ▼
    Kairo
```

Adding or changing a subscriber does not require modifying the producer.

### Multi-tenant delivery policies

Different subscribers can have different:

```text
Destinations
Authentication
Filters
Transformations
Rate limits
Retry policies
```

### Operational visibility

Kairo provides a complete event and delivery history:

```text
order.created
evt_123

Source
Orders API

Matched subscriptions
3

Deliveries

✓ ACME
  HTTP 200
  84 ms

✓ Globex
  Kafka
  12 ms

↻ Umbrella
  attempt 1 — timeout
  attempt 2 — HTTP 503
  next retry — 14:37
```

---

## What Kairo Is Not

Kairo is intentionally **not**:

- a Kafka replacement
- a general message broker
- a workflow engine
- an ESB
- an iPaaS
- a Zapier replacement
- a general-purpose API gateway

Kairo focuses on one problem:

> **Reliably delivering application events to subscribers and external systems.**

---

## Architecture

Kairo separates durable state from asynchronous work.

```text
                          ┌─────────────────┐
                          │ React Dashboard │
                          └────────┬────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │    Kairo API    │
                          │ ASP.NET Core 8  │
                          └────────┬────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │   PostgreSQL    │
                          │                 │
                          │ source of truth │
                          └────────┬────────┘
                                   │
                          Transactional Outbox
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ NATS JetStream  │
                          │ durable work    │
                          └────────┬────────┘
                                   │
                    ┌──────────────┼──────────────┐
                    ▼              ▼              ▼
               Subscription    Delivery       Retry
                 Worker         Worker        Scheduler
                    │              │
                    │              ▼
                    │        Destinations
                    │
                    └──────────────┘
```

### PostgreSQL

PostgreSQL is the durable source of truth for:

- sources
- events
- subscribers
- subscriptions
- destinations
- deliveries
- delivery attempts
- retry state
- outbox messages

### NATS JetStream

JetStream is used for durable asynchronous work distribution.

It is **not** the canonical business database.

```text
PostgreSQL = durable state
JetStream  = work distribution
Memory     = performance/cache
```

### Transactional Outbox

Events and the work required to process them are written atomically.

```text
BEGIN

INSERT event
INSERT outbox message

COMMIT

     ↓

Outbox Worker

     ↓

NATS JetStream
```

If JetStream is temporarily unavailable, accepted events remain safely stored in PostgreSQL and are published when the broker becomes available again.

### Delivery Semantics

Kairo uses **at-least-once processing**.

Exactly-once HTTP delivery cannot be guaranteed across arbitrary external systems.

Kairo therefore provides stable identifiers such as:

```text
Event ID
Delivery ID
Attempt number
```

so consumers can implement idempotency.

---

## Technology

### Backend

- .NET 8
- ASP.NET Core
- C#
- Dapper
- Npgsql
- PostgreSQL

### Messaging

- NATS JetStream

### Frontend

- React
- TypeScript
- Vite

### Observability

- OpenTelemetry

### Deployment

- Docker
- Docker Compose

Kubernetes support may be added later but is not required to run Kairo.

---

## Project Architecture

```text
kairo-event/
│
├── src/
│   ├── Kairo.Api/
│   ├── Kairo.Worker/
│   ├── Kairo.Application/
│   ├── Kairo.Domain/
│   ├── Kairo.Infrastructure/
│   └── Kairo.Contracts/
│
├── dashboard/
│   └── kairo-dashboard/
│
├── tests/
│   ├── Kairo.UnitTests/
│   ├── Kairo.IntegrationTests/
│   └── Kairo.ArchitectureTests/
│
├── deploy/
│   └── docker/
│
└── Kairo.sln
```

The backend follows a modular architecture with vertical application slices.

```text
Kairo.Api
    │
    ▼
Kairo.Application
    │
    ▼
Kairo.Domain

Kairo.Infrastructure
    │
    ├── PostgreSQL / Dapper
    ├── NATS JetStream
    ├── HTTP delivery
    └── OpenTelemetry
```

Workers use the same application layer and can later be scaled independently.

---

## Self-Hosting

Kairo is designed to be self-hosted from the beginning.

The default deployment should remain simple:

```text
Kairo API
Kairo Worker
React Dashboard
PostgreSQL
NATS JetStream
```

with:

```bash
docker compose up -d
```

Small installations can run a single worker process.

Larger installations can independently scale delivery workers:

```text
API                 × 2
Subscription Worker × 2
Delivery Worker     × 10
Retry Scheduler     × 1
PostgreSQL
NATS JetStream
```

without changing the Kairo programming model.

---

## Roadmap

### v0.1 — Reliable HTTP delivery

- [ ] Project foundation
- [ ] Source management
- [ ] Event ingestion API
- [ ] Event persistence
- [ ] Subscribers
- [ ] Subscriptions
- [ ] HTTP destinations
- [ ] HTTP delivery
- [ ] Delivery attempts
- [ ] Retry engine
- [ ] Dead deliveries
- [ ] Manual replay
- [ ] Transactional outbox
- [ ] NATS JetStream workers
- [ ] Docker Compose

### v0.2 — Operational Dashboard

- [ ] React dashboard
- [ ] Event explorer
- [ ] Event details
- [ ] Delivery timeline
- [ ] Failed delivery explorer
- [ ] Retry / replay
- [ ] Destination health
- [ ] Basic metrics

### v0.3 — Delivery Policies

- [ ] Subscription filters
- [ ] Transformations
- [ ] HMAC signing
- [ ] Rate limiting
- [ ] Destination concurrency limits
- [ ] Circuit breakers
- [ ] Configurable retry policies

### v0.4 — Additional Destinations

- [ ] NATS
- [ ] Kafka
- [ ] AWS SQS
- [ ] RabbitMQ

### Future

- Infrastructure as code
- Kairo CLI
- Configuration diff / apply
- Event schemas and versions
- Delivery contract validation
- Advanced replay
- OpenTelemetry tracing
- High-availability deployment
- Additional SDKs

---

## Design Principles

Kairo is built around several principles.

### Accepted events should not disappear

If Kairo acknowledges an event, component failures should not silently lose it.

### PostgreSQL protects correctness

Canonical events, deliveries, attempts, subscriptions, and retry state are durable.

### Memory protects performance

Subscriptions, compiled filters, destination configuration, and other safe data may be cached in memory.

### Brokers distribute work

JetStream provides asynchronous work distribution and worker crash recovery without replacing PostgreSQL as Kairo's business state.

### Broken destinations stay isolated

One unavailable subscriber should not consume all system capacity or block healthy subscribers.

### Start simple, scale horizontally

Kairo should work with Docker Compose on one machine and scale by adding stateless API and worker processes.

---

## Status

> **Early development**

Kairo is currently under active development and is not yet production ready.

APIs, schemas, architecture, and configuration may change before the first stable release.

---

## Contributing

Kairo is developed publicly and contributions are welcome.

Useful areas include:

- .NET backend
- React dashboard
- event delivery
- PostgreSQL / Dapper
- NATS JetStream
- integrations
- testing
- observability
- documentation
- Docker / self-hosting

Please open an issue before starting large architectural changes.

---

## License

Kairo Event is intended to be source-available and self-hostable.

The licensing model is being finalized.

See [`LICENSE`](LICENSE) for the terms that apply to the current version.

---

**Kairo Event**

> Publish once. Deliver reliably. Own the infrastructure.

**Kairo Event — reliable event infrastructure in every direction.**




