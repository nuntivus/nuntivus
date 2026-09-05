<img width="2172" height="724" alt="Kairo Event" src="https://github.com/user-attachments/assets/4736401d-056f-4b5d-b536-bafac2f48e71" />

# ✈️ Kairo Server

**Self-hosted event delivery infrastructure for SaaS and platform teams.**

> **Publish once. Deliver reliably. Own the infrastructure.**

Kairo Server is the core backend of **Kairo Event**.

It receives events from applications, APIs, webhooks, and messaging systems, then reliably delivers those events to subscribers over protocols such as HTTP, Kafka, NATS, RabbitMQ, and SQS.

Kairo centralizes the infrastructure teams often end up rebuilding themselves:

- persistence
- routing
- retries
- rate limiting
- signing
- delivery history
- replay
- failure handling
- subscriber isolation

---

## Why Kairo?

Event delivery usually starts simple:

```text
event
  │
  ▼
HTTP POST
  │
  ▼
consumer
```

Then production requirements appear:

```text
retries
rate limits
authentication
signing
delivery history
replay
endpoint health
failure isolation
tenant-specific rules
multiple delivery protocols
```

At that point, the application is no longer implementing a simple webhook sender.

It is building **event delivery infrastructure**.

Kairo provides that infrastructure as a self-hosted platform.

Your systems publish events.

Kairo handles how subscribers receive them.

---

## Core Model

```text
Source
  │
  ▼
Event
  │
  ▼
Subscriber
  │
  ▼
Delivery
```

### Source

A **Source** represents where an event enters Kairo.

Examples:

```text
HTTP API
Webhook
Application / SDK
NATS
Kafka
RabbitMQ
SQS
```

Different sources can use different protocols.

Once accepted, Kairo converts incoming data into a common internal event model.

---

### Event

An **Event** represents something that happened in a system.

Examples:

```text
order.created
invoice.paid
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

Accepted events are immutable.

---

### Subscriber

A **Subscriber** describes who wants to receive events and how Kairo should deliver them.

A subscriber may define:

```text
event types
filters
transformations
protocol
endpoint or connection
authentication
rate limit
retry policy
enabled state
```

Example:

```text
Subscriber: ACME

Events:
order.created
order.updated

Filter:
region == "EU"

Protocol:
HTTP

Endpoint:
https://api.acme.com/events

Authentication:
HMAC

Rate limit:
10 requests/second

Retry policy:
exponential, up to 48 hours
```

Subscribers are not limited to HTTP.

A subscriber may receive events through:

```text
HTTP
Kafka
NATS
RabbitMQ
SQS
```

---

### Delivery

A **Delivery** represents one event being delivered to one subscriber.

```text
event evt_123
     │
     ├── delivery dlv_001 → ACME       ✓ delivered
     ├── delivery dlv_002 → Globex     ↻ retrying
     └── delivery dlv_003 → Umbrella   ✓ delivered
```

Each delivery has its own:

```text
state
attempt history
retry schedule
response information
error information
timestamps
```

A failure for one subscriber does not block delivery to others.

---

## Example

```text
                         SOURCES

              HTTP       Stripe       Kafka
                │           │           │
                └───────────┼───────────┘
                            │
                            ▼
                     ┌─────────────┐
                     │    Kairo    │
                     └──────┬──────┘
                            │
                            ▼
                          Event
                            │
              ┌─────────────┼─────────────┐
              │             │             │
              ▼             ▼             ▼
            ACME          Globex       Umbrella
              │             │             │
              ▼             ▼             ▼
            HTTP          Kafka           SQS
```

If ACME is unavailable:

```text
ACME       503 → retrying
Globex     delivered
Umbrella   delivered
```

Only ACME's delivery is affected.

The other subscribers continue normally.

---

## What Problem Does Kairo Solve?

Without Kairo, applications often accumulate infrastructure such as:

```text
webhook workers
retry schedulers
queue consumers
delivery databases
signing logic
rate limiting
replay scripts
dead-letter handling
monitoring
customer-specific delivery code
```

With Kairo:

```text
Application
    │
    │ order.created
    ▼
  Kairo
    │
    ├── ACME      → HTTP
    ├── Globex    → Kafka
    ├── Umbrella  → SQS
    └── Partner   → NATS
```

The producer does not need to know how every subscriber wants to receive the event.

Kairo owns the delivery layer.

---

## Positioning

Kairo focuses on:

> **Reliable event delivery between producers and external or independent consumers.**

Kairo is not intended to be:

- a Kafka replacement
- a general-purpose message broker
- a workflow engine
- an ESB
- an iPaaS
- a generic automation platform
- a general-purpose API gateway

Kairo focuses on the boundary where applications need to deliver events reliably to other systems.

---

## Architecture

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
         Matching       Delivery       Retry
          Worker         Worker      Scheduler
             │             │             │
             └─────────────┼─────────────┘
                           │
                           ▼
                     PostgreSQL
```

The responsibilities are intentionally separated:

```text
PostgreSQL
= durable business state

NATS JetStream
= asynchronous work distribution

Memory
= caching and hot-path performance
```

PostgreSQL remains the source of truth.

JetStream is used to distribute asynchronous work between workers.

---

## Event Flow

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
Commit transaction
  │
  ▼
Publish work to JetStream
  │
  ▼
Match subscribers
  │
  ▼
Create deliveries
  │
  ▼
Dispatch delivery
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

Kairo uses **at-least-once processing**.

An accepted event should not disappear because a worker, broker, network connection, or subscriber temporarily fails.

Consumers should therefore be designed with idempotency in mind.

---

## Technology

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

## Self-Hosting

Kairo is designed to be self-hosted from the beginning.

A minimal deployment consists of:

```text
Kairo API
Kairo Worker
PostgreSQL
NATS JetStream
```

and is intended to remain deployable with:

```bash
docker compose up -d
```

Small installations can run with a minimal number of processes.

Larger installations can scale API and worker instances independently without changing the Kairo programming model.

---

## Project Structure

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

## Related Projects

The Kairo Event ecosystem may include separate repositories for:

```text
kairo-server       Core API and delivery runtime
kairo-dashboard    Web administration interface
kairo-sdk-dotnet   .NET SDK
kairo-sdk-js       JavaScript / TypeScript SDK
kairo-cli          Command-line tools
kairo-docs         Documentation
```

---

## Status

> [!WARNING]
> Kairo is currently in early development and is **not yet production ready**.

APIs, schemas, configuration, and architecture may change before the first stable release.

---

## License

Kairo Event is source-available and self-hostable.

See [`LICENSE`](LICENSE) for the terms that apply to the current version.
