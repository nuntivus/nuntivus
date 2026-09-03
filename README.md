<img width="1774" height="887" alt="image" src="https://github.com/user-attachments/assets/1f65413d-cd2b-4b03-adca-9ad09ca1d5c8" />

**Open-source bidirectional event infrastructure with a built-in operational control plane.**

Kairo Event is a developer-first platform for receiving, routing, transforming, observing, retrying, replaying, and delivering events between applications, services, APIs, webhooks, and AI agents.

The goal is simple:

> One reliable layer for everything entering and leaving your application.

Kairo Event combines a backend event gateway with a web dashboard for operating event flows in real time.

---

## Why Kairo Event?

Modern applications communicate with many external systems:

* Webhooks
* REST APIs
* SaaS platforms
* Microservices
* Background workers
* Internal services
* Event-driven systems
* AI agents

Every integration usually requires custom code for:

* retries
* authentication
* signatures
* logging
* routing
* payload transformations
* error handling
* delivery tracking
* replay
* observability

Kairo Event aims to provide these capabilities as reusable infrastructure.

Instead of rebuilding reliability and debugging tools inside every application, developers can route inbound and outbound communication through Kairo Event.

---

# 🎯 Project Target

Kairo Event aims to become an open-source, bidirectional event infrastructure platform for modern applications, services, APIs, and AI agents.

The project has two main parts:

```text
Kairo Event
│
├── Event Infrastructure
│   ├── Receive
│   ├── Verify
│   ├── Store
│   ├── Route
│   ├── Transform
│   ├── Retry
│   ├── Replay
│   └── Deliver
│
└── Control Plane
    ├── Logs
    ├── Event Explorer
    ├── Retry Operations
    ├── Replay Operations
    ├── Metrics
    ├── Routes
    ├── Sources
    ├── Destinations
    └── Configuration
```

The target is not to build only a webhook server.

The long-term goal is to create a complete event communication and operations layer.

---

# Architecture

Kairo Event sits between your application and the systems it communicates with.

<img width="1448" height="1086" alt="image" src="https://github.com/user-attachments/assets/96be42ae-5436-4f64-99a1-ccd77acb6111" />

Kairo Event supports traffic in both directions.

---

# Inbound Events

External systems can send events into Kairo Event.

```text
External Service
       │
       ▼
   Kairo Event
       │
       ▼
 Your Application
```

Examples:

```text
Stripe
   ↓
Kairo Event
   ↓
Billing Service
```

```text
GitHub
   ↓
Kairo Event
   ↓
Deployment Service
```

Inbound capabilities can include:

* webhook reception
* signature verification
* authentication
* event persistence
* deduplication
* filtering
* routing
* payload transformation
* event forwarding
* replay

---

# Outbound Events

Applications can use Kairo Event to deliver events to external systems.

```text
Your Application
       │
       ▼
   Kairo Event
       │
       ▼
External Service
```

Examples:

```text
Application
    ↓
Kairo Event
    ↓
Customer Webhook
```

```text
AI Agent
    ↓
Kairo Event
    ↓
External API
```

Outbound capabilities can include:

* reliable HTTP delivery
* configurable retries
* exponential backoff
* timeouts
* signing
* custom headers
* delivery history
* rate limiting
* dead-letter handling
* circuit breakers
* delivery observability

---

# 🖥️ Web Dashboard

Kairo Event includes a frontend control plane for managing and observing the platform.

The dashboard should make event operations possible without interacting directly with the database or command line.

The initial frontend will focus on:

* viewing events
* inspecting payloads
* viewing delivery attempts
* retrying failed deliveries
* replaying historical events
* searching logs
* filtering events
* managing sources
* managing destinations
* managing routes
* sending test events
* viewing basic metrics

The dashboard will be built with **TypeScript and React/Next.js**.

---

# Event Explorer

The Event Explorer is intended to become one of the main operational screens.

Example:

```text
EVENTS

Status   Event                 Source      Destination    Attempts   Latency

✓        payment.succeeded     Stripe      Billing API    1          83ms
✕        order.created         Shopify     CRM            4          2.1s
↻        customer.updated      Internal    Webhook        2          320ms
✓        deployment.ready      GitHub      Deploy API     1          95ms
```

Users should be able to filter by:

* status
* source
* destination
* event type
* time range
* response code
* retry count

---

# Event Details

Each event should have a detailed operational view.

Example:

```text
payment.succeeded

Status: Delivered
Source: Stripe
Destination: Billing API
Attempts: 1
Latency: 83ms

Actions

[ Replay ]
[ Retry ]
[ Send Again ]
[ Send To Another Destination ]
```

The event page can include:

## Payload

```json
{
  "type": "payment.succeeded",
  "customer_id": "cus_123",
  "amount": 4900
}
```

## Delivery Timeline

```text
14:22:01  Event received
14:22:01  Signature verified
14:22:01  Event persisted
14:22:01  Route matched
14:22:02  Delivery started
14:22:02  Response received: HTTP 200
14:22:02  Event delivered
```

## Response

```text
Status: 200
Latency: 83ms
```

---

# Retry Engine

Retries are a core part of Kairo Event.

When a destination fails, Kairo Event should be able to retry automatically.

Example:

```text
Attempt 1
   ↓
HTTP 500
   ↓
Wait
   ↓
Attempt 2
   ↓
Timeout
   ↓
Wait
   ↓
Attempt 3
   ↓
HTTP 200
   ↓
Delivered
```

Retry strategies may eventually support:

* fixed delay
* exponential backoff
* maximum attempts
* retry based on HTTP status
* custom retry policies
* manual retry
* batch retry

---

# Replay

Stored events should be replayable.

This is useful when:

* a service was temporarily unavailable
* a bug was fixed
* a destination was misconfigured
* a developer wants to test a new route

CLI example:

```bash
kairo events replay <event-id>
```

Dashboard:

```text
[ Replay Event ]
```

---

# Sources

A source represents where events come from.

Examples:

* Stripe
* GitHub
* Shopify
* Internal applications
* Custom webhooks
* AI agents

Example source:

```text
Source

Name: Stripe Production
Type: Stripe
Endpoint: /inbound/stripe

Verification:
✓ Signature verification enabled
```

---

# Destinations

A destination represents where Kairo Event delivers events.

Examples:

* customer webhook
* internal API
* Slack
* Discord
* microservice
* external REST API

Example:

```text
Destination

Name: Billing Service
URL: https://billing.internal/events

Timeout: 10s
Retries: 5
Signing: HMAC
```

---

# Routes

Routes connect sources and events to destinations.

Example:

```text
stripe.payment_failed

       │
       ├──> Billing Service
       ├──> Slack
       └──> Customer Webhook
```

A future route configuration may look like:

```yaml
source: stripe

event: invoice.payment_failed

destinations:
  - billing-api
  - finance-slack

retry:
  attempts: 5
  strategy: exponential
```

---

# Transformations

Kairo Event should be able to transform payloads before delivery.

```text
Incoming Event
      │
      ▼
 Transformation
      │
      ▼
Outgoing Payload
```

Example:

```json
{
  "customer": {
    "id": "cus_123"
  }
}
```

could become:

```json
{
  "customer_id": "cus_123"
}
```

Transformations should eventually be testable directly from the dashboard.

---

# Logs

Kairo Event should provide searchable operational logs.

Examples:

```text
INFO  Event evt_12 received from stripe
INFO  Route billing-route matched
INFO  Delivery del_88 started
WARN  Delivery del_88 returned HTTP 500
INFO  Retry scheduled in 30 seconds
INFO  Retry attempt 2 started
INFO  Delivery successful
```

The dashboard should allow searching by:

* event ID
* delivery ID
* source
* destination
* status
* error
* time range

---

# Metrics

The dashboard should provide useful operational metrics.

Examples:

```text
Events / minute
Successful deliveries
Failed deliveries
Retry rate
Average latency
P95 latency
Pending deliveries
Dead-letter events
```

Future dashboards may also include:

* throughput by source
* throughput by destination
* error rate over time
* response code distribution
* top failing destinations
* queue depth
* worker utilization

---

# Dead-Letter Queue

Events that cannot be delivered after all retry attempts should be moved into a dead-letter state.

Example:

```text
Failed Event
     │
     ▼
Retry 1
     │
     ▼
Retry 2
     │
     ▼
Retry 3
     │
     ▼
Dead Letter
```

The dashboard should allow operators to:

* inspect the failure
* modify the destination
* retry the event
* replay the event
* mark the event as resolved

---

# Initial Milestone

The first milestone should remain small and useful.

## v0.1

The initial lifecycle:

```text
Receive
   ↓
Store
   ↓
Inspect
   ↓
Forward
   ↓
Retry
   ↓
Replay
```

Initial backend goals:

* [ ] TypeScript project setup
* [ ] Fastify API
* [ ] Receive HTTP events
* [ ] Persist events
* [ ] Create destinations
* [ ] Deliver events
* [ ] Record delivery attempts
* [ ] Retry failed requests
* [ ] Replay events
* [ ] Basic API validation
* [ ] Docker support
* [ ] Tests

Initial frontend goals:

* [ ] Dashboard shell
* [ ] Event list
* [ ] Event details
* [ ] Payload viewer
* [ ] Delivery attempt timeline
* [ ] Retry button
* [ ] Replay button
* [ ] Source list
* [ ] Destination list
* [ ] Basic logs
* [ ] Basic metrics

---

# Roadmap

## v0.1 — Core Event Lifecycle

Backend:

* event ingestion
* persistence
* destinations
* HTTP delivery
* retries
* replay
* delivery logs

Frontend:

* event explorer
* event details
* retry
* replay
* delivery timeline

---

## v0.2 — Operational Dashboard

* sources management
* destinations management
* routes management
* advanced event filters
* search
* live logs
* dashboard metrics
* test event sender
* improved CLI
* Docker image

---

## v0.3 — Integrations

Initial integrations:

* Stripe
* GitHub
* Slack
* Discord
* Generic Webhook

Features:

* signature verification
* provider-specific parsing
* integration SDK
* integration templates

---

## v0.4 — Advanced Routing

* event filters
* multiple destinations
* transformations
* conditional routes
* rate limiting
* dead-letter queue
* retry policies

---

## v0.5 — Distributed Workers

* PostgreSQL
* queue abstraction
* Redis/BullMQ support
* separate delivery workers
* horizontal worker scaling
* worker dashboard
* metrics
* OpenTelemetry

---

## v0.6 — Operations

* alerting
* bulk retries
* bulk replay
* log retention controls
* event retention controls
* destination health
* route analytics
* worker health
* queue visibility

---

## v1.0 — Distributed Event Infrastructure

* production-ready self-hosting
* high availability
* distributed workers
* multi-region architecture
* event ordering
* stronger delivery guarantees
* advanced observability
* agent integrations
* enterprise-ready operational controls

---

# AI Agents

AI agents increasingly need to communicate with applications and external services.

Kairo Event should provide a reliable layer for those interactions.

Agents may eventually be able to:

* create sources
* register destinations
* create routes
* inspect events
* inspect failures
* retry deliveries
* replay events
* trigger external APIs
* query operational state

Example:

```text
AI Agent
    │
    ▼
Kairo Event
    │
    ├──> GitHub
    ├──> Internal API
    ├──> Slack
    └──> Customer Webhook
```

Future versions may expose agent-friendly APIs and tool interfaces so agents can safely operate event infrastructure programmatically.

---

# Tech Stack

The initial implementation will use:

## Backend

* Node.js
* TypeScript
* Fastify
* Zod
* SQLite for early development
* PostgreSQL for production
* Vitest

## Frontend

* React
* Next.js
* TypeScript

## Infrastructure

* Docker
* PostgreSQL
* Redis / BullMQ when asynchronous delivery requires it
* OpenTelemetry later

The project should avoid introducing complex infrastructure before it is necessary.

---

# Repository Structure

Kairo Event will use a modular monorepo architecture.

```text
kairo-event/
│
├── apps/
│   ├── api/
│   │
│   ├── worker/
│   │
│   └── dashboard/
│
├── packages/
│   ├── core/
│   ├── database/
│   ├── delivery/
│   ├── routing/
│   ├── integrations/
│   ├── sdk/
│   └── ui/
│
├── examples/
│
├── docs/
│
├── tests/
│
├── CONTRIBUTING.md
├── ROADMAP.md
├── LICENSE
├── package.json
└── README.md
```

The architecture should remain modular so contributors can work independently on:

* integrations
* frontend components
* SDKs
* storage adapters
* queue adapters
* routing
* delivery
* metrics
* observability
* deployment tooling

---

# Dashboard Navigation

The long-term dashboard may look like:

```text
Kairo Event

Overview

Events
├── All Events
├── Failed
├── Retrying
└── Dead Letter

Sources

Destinations

Routes

Transformations

Deliveries

Logs

Metrics

Workers

Settings
```

The dashboard should be an operational tool, not only a visualization layer.

Users should be able to understand what happened and take action directly.

---

# Design Principles

## Open

The core platform should be open source.

## Self-hostable

Developers should be able to run Kairo Event on their own infrastructure.

## Developer-first

The API, CLI, dashboard, documentation, and local development experience should be easy to understand.

## Operational

When an event fails, the user should be able to understand exactly why and take action.

## Modular

Storage, queues, transports, integrations, routing, and delivery should be replaceable.

## Reliable

Durability and predictable failure handling are more important than unnecessary complexity.

## Observable

Every event should have a clear history.

## AI-friendly

Applications and agents should be able to interact with the platform programmatically.

---

# Long-Term Vision

Kairo Event should grow beyond a webhook utility.

The long-term vision is a complete event communication and operations layer connecting:

```text
Applications
APIs
Webhooks
Microservices
SaaS Platforms
Queues
Workers
Databases
AI Agents
```

through one infrastructure layer.

```text
Anything
   │
   ▼
┌─────────────┐
│ Kairo Event │
└──────┬──────┘
       │
       ▼
    Anything
```

Kairo Event should provide both:

```text
Data Plane
Receive → Route → Transform → Deliver

Control Plane
Inspect → Search → Retry → Replay → Monitor → Configure
```

---

# Contributing

Kairo Event is being built in the open.

Contributions are welcome from the beginning.

You can contribute to:

* backend development
* frontend dashboard
* event routing
* retry logic
* integrations
* SDKs
* documentation
* tests
* storage adapters
* queue adapters
* observability
* Docker
* Kubernetes
* UI/UX
* performance

Look for issues labeled:

```text
good first issue
help wanted
backend
frontend
integration
documentation
observability
infrastructure
```

For large features or architectural changes, please open an issue first so the design can be discussed.

---

# Development

Clone the repository:

```bash
git clone https://github.com/YOUR_USERNAME/kairo-event.git
cd kairo-event
```

Install dependencies:

```bash
pnpm install
```

Run development:

```bash
pnpm dev
```

Run tests:

```bash
pnpm test
```

---

# License

Kairo Event is licensed under the **Apache License 2.0**.

This allows developers and companies to use, modify, distribute, and build on the project while retaining the license and patent protections provided by Apache 2.0.

See `LICENSE` for complete terms.

---

# Project Status

Kairo Event is currently in **early development**.

APIs, architecture, database schemas, and interfaces may change quickly before the first stable release.

If the project interests you:

* ⭐ Star the repository
* 🐛 Open an issue
* 💡 Propose a feature
* 🔧 Submit a pull request
* 🎨 Help with the dashboard
* 📚 Improve the documentation
* 🔌 Build an integration

---

# Kairo Event

**Reliable event infrastructure in every direction.**

Receive. Route. Observe. Retry. Replay. Deliver.




