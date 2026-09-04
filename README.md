<img width="1774" height="887" alt="Kairo Event" src="https://github.com/user-attachments/assets/1f65413d-cd2b-4b03-adca-9ad09ca1d5c8" />

# ✈️ Kairo Event

**Self-hosted event delivery infrastructure for SaaS and platform teams.**

> **Publish once. Deliver reliably. Own the infrastructure.**

Kairo helps applications and platforms receive events from multiple sources and reliably deliver them to subscribers across HTTP, queues, and event brokers.

It centralizes the infrastructure teams usually end up rebuilding themselves: persistence, routing, retries, rate limits, signing, delivery history, replay, and failure handling.

## Why Kairo?

Event delivery often starts simple:

```text
event
  ↓
HTTP POST
  ↓
consumer
````

Then production requirements appear:

* retries
* rate limiting
* signing
* replay
* delivery logs
* endpoint health
* tenant-specific rules
* multiple delivery protocols
* failure isolation

At that point, teams are no longer building a simple webhook sender.

They are building event delivery infrastructure.

**Kairo provides that infrastructure as a self-hosted platform.**

Your systems emit events.

Kairo handles how subscribers receive them.

## Core Model

```text
Source
  ↓
Event
  ↓
Subscriber
  ↓
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

Kairo normalizes events from different sources into a common internal event model.

### Event

An **Event** represents something that happened.

Examples:

```text
order.created
invoice.paid
user.deleted
deployment.completed
```

Events are immutable once accepted.

### Subscriber

A **Subscriber** defines who should receive an event, under which rules, and how Kairo should deliver it.

A subscriber can define:

```text
event types
filter
transformation
protocol
endpoint / connection
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

A subscriber may also use another delivery protocol:

```text
Kafka
NATS
RabbitMQ
SQS
```

### Delivery

A **Delivery** represents one event being delivered to one subscriber.

```text
event evt_123
     │
     ├── delivery dlv_001 → ACME       ✓ delivered
     ├── delivery dlv_002 → Globex     ↻ retrying
     └── delivery dlv_003 → Umbrella   ✓ delivered
```

Each delivery has its own state and attempt history.

## Example

```text
                        SOURCES

             HTTP      Stripe      Kafka
               │          │          │
               └──────────┼──────────┘
                          ▼
                        Kairo
                          │
                          ▼
                        Event
                          │
              ┌───────────┼───────────┐
              ▼           ▼           ▼

            ACME        Globex     Umbrella

            HTTP         Kafka        SQS
```

If one subscriber is unavailable:

```text
ACME       503 → retrying
Globex     delivered
Umbrella   delivered
```

Only that subscriber's delivery is affected.

The others continue normally.

## What Problem Does Kairo Solve?

Without Kairo, applications often accumulate:

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
    ├── ACME → HTTP
    ├── Globex → Kafka
    ├── Umbrella → SQS
    └── Partner → NATS
```

The producer does not need to know how every subscriber wants to receive the event.

Kairo owns that delivery contract.

## Positioning

Kairo focuses on:

> **Reliable event delivery between producers and external or independent consumers.**

Kairo is not intended to be:

* a Kafka replacement
* a general-purpose message broker
* a workflow engine
* an ESB
* an iPaaS
* a generic automation platform
* a general-purpose API gateway

Kairo focuses on the unreliable boundary between event producers and their subscribers.

## Architecture

```text
                         Sources
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
             ▼             ▼             ▼
         Matching       Delivery       Retry
          Worker         Worker      Scheduler
             │             │
             └─────────────┼─────────────┐
                           ▼             ▼
                      Subscribers    PostgreSQL
```

The main responsibilities are:

```text
PostgreSQL
= durable business state

NATS JetStream
= asynchronous work distribution

Memory
= caching and performance
```

## Event Flow

```text
Source
  │
  ▼
Event accepted
  │
  ▼
Persist event + outbox
  │
  ▼
Commit
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
Deliver
  │
  ├── success
  │
  └── failure
         │
         ▼
       retry
         │
         ▼
       dead
```

Kairo uses **at-least-once processing**.

Accepted events should not disappear because a worker, broker, or destination temporarily fails.

## Technology

```text
Backend      .NET 8 / ASP.NET Core
Database     PostgreSQL
Data access  Dapper + Npgsql
Messaging    NATS JetStream
Frontend     React + TypeScript + Vite
Deployment   Docker / Docker Compose
Telemetry    OpenTelemetry
```

## Self-Hosting

Kairo is designed to be self-hosted from the beginning.

A standard deployment includes:

```text
Kairo API
Kairo Worker
React Dashboard
PostgreSQL
NATS JetStream
```

and should remain deployable with:

```bash
docker compose up -d
```

Small installations can run a single worker process.

Larger installations can scale workers independently without changing the Kairo programming model.

## Project Structure

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
│
└── deploy/
```

The backend uses a modular architecture with vertical application slices.

## Status

Kairo is in early development and is not yet production ready.

APIs, schemas, and architecture may change before the first stable release.

## License

Kairo Event is source-available and self-hostable.

See [`LICENSE`](LICENSE) for the terms that apply to the current version.







