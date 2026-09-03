<img width="1774" height="887" alt="Kairo Event" src="https://github.com/user-attachments/assets/1f65413d-cd2b-4b03-adca-9ad09ca1d5c8" />

**Open-source bidirectional event infrastructure for apps, services, APIs, webhooks, and AI agents.**

> One reliable layer for everything entering and leaving your application.

Kairo Event receives, stores, routes, transforms, retries, replays, and delivers events while providing a web dashboard for monitoring and operations.

### Progress

`v0.1 ░░░░░░░░░░ 0%`

---

## 🎯 Goal

Build an open-source event gateway with two main components:

### Data Plane

```text
Receive → Verify → Store → Route → Transform → Deliver
                                      ↓
                                    Retry
````

### Control Plane

```text
Inspect → Search → Logs → Retry → Replay → Metrics → Configure
```

Kairo Event supports both directions:

```text
External Services
       ↓
   Kairo Event
       ↓
 Your Application
```

and:

```text
Your Application
       ↓
   Kairo Event
       ↓
External Services
```

---

## ✨ Features

* Inbound webhooks
* Outbound event delivery
* Event persistence
* Routing
* Payload transformations
* Automatic retries
* Event replay
* Signature verification
* Delivery logs
* Dead-letter events
* Metrics and observability
* Web dashboard
* Self-hosting
* AI-agent integrations

---

## 🖥 Dashboard

The Kairo Event dashboard will allow developers to:

* Inspect events and payloads
* View delivery history
* Retry failed events
* Replay events
* Search logs
* Manage sources
* Manage destinations
* Configure routes
* Monitor metrics

---

## 🏗 Architecture

```text
            External Systems
 Stripe / GitHub / APIs / Agents
                 │
                 ▼
        ┌─────────────────┐
        │   Kairo Event   │
        │                 │
        │ Receive         │
        │ Verify          │
        │ Store           │
        │ Route           │
        │ Transform       │
        │ Retry           │
        │ Deliver         │
        └────────┬────────┘
                 │
                 ▼
        Apps / APIs / Webhooks

                 ↕
        ┌─────────────────┐
        │  Control Plane  │
        │ Logs / Metrics  │
        │ Retry / Replay  │
        │ Configuration   │
        └─────────────────┘
```

---

## 🚀 v0.1

### Backend

* [ ] TypeScript setup
* [ ] Fastify API
* [ ] Receive events
* [ ] Store events
* [ ] Destinations
* [ ] HTTP delivery
* [ ] Retry engine
* [ ] Replay
* [ ] Delivery history
* [ ] Tests
* [ ] Docker

### Frontend

* [ ] Dashboard
* [ ] Event explorer
* [ ] Event details
* [ ] Payload viewer
* [ ] Retry / replay
* [ ] Logs
* [ ] Basic metrics

---

## 🗺 Roadmap

* **v0.1** — Core event lifecycle
* **v0.2** — Operational dashboard
* **v0.3** — Stripe, GitHub, Slack and Discord integrations
* **v0.4** — Advanced routing and transformations
* **v0.5** — Distributed workers and queues
* **v1.0** — Production-ready distributed event infrastructure

---

## 🛠 Tech Stack

### Backend

* .Net
* C#
* PostgreSQL

### Frontend

* Next.js
* React
* TypeScript

### Infrastructure

* Docker
* PostgreSQL
* Redis / BullMQ later
* OpenTelemetry later

---

## 📁 Project Structure

```text
kairo-event/
├── apps/
│   ├── api/
│   ├── worker/
│   └── dashboard/
│
├── packages/
│   ├── core/
│   ├── database/
│   ├── delivery/
│   ├── routing/
│   ├── integrations/
│   └── sdk/
│
├── docs/
├── examples/
└── tests/
```

---

## 🤝 Contributing

Kairo Event is being built in the open.

Contributions are welcome in:

* Backend
* Frontend
* Integrations
* SDKs
* Documentation
* Testing
* Observability
* Infrastructure

Look for issues labeled:

`good first issue` `help wanted` `backend` `frontend` `integration`

---

## 📜 License

Licensed under the **Apache License 2.0**.

---

## 🚧 Status

Kairo Event is currently in **early development**.

APIs and architecture may change before the first stable release.

⭐ Star the project
🐛 Open an issue
🔧 Submit a PR
💡 Suggest a feature

---

**Kairo Event — reliable event infrastructure in every direction.**




