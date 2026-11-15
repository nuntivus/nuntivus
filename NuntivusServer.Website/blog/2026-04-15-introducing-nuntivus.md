---
slug: introducing-nuntivus
title: Introducing Nuntivus
authors: [nuntivus]
tags: [project, architecture]
description: An open-source, self-hosted delivery layer between your business events and your customers.
---

Every customer has a different integration story. One wants an HTTP webhook, another uses Kafka, and a third needs messages delivered to a queue. Application code can quickly become a collection of routing conditions, payload adapters and retry loops.

Nuntivus explores a different approach: a dedicated, self-hosted customer event delivery layer.

<!-- truncate -->

## Publish once, define delivery separately

Sources bring events into the system. Customer contracts describe which events should be delivered and how to filter, validate and transform them. Shared destinations hold delivery configuration for HTTP, Kafka, RabbitMQ, NATS and SQS.

The administration dashboard brings the configuration and delivery history together, with connection tests, dry runs, retries, replays, SLOs and alerts.

## Reliability has explicit boundaries

Nuntivus processes events at least once. Consumers must handle duplicate deliveries, and external side effects are not exactly once. Operational visibility is important, but it does not eliminate the need for backups, safe credential handling and downstream idempotency.

## Help shape the project

The project is in early development and is not yet production ready. APIs and schemas may change. Start with a local evaluation, review the documented limitations, and share feedback or issues on GitHub.

- [Get started locally](/docs/quick-start)
- [Read the architecture](/docs/reference/architecture)
- [Explore the repository](https://github.com/nuntivus/nuntivus-server)
