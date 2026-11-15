---
title: What is Nuntivus?
sidebar_label: Introduction
---

# Customer events, delivered your way

Nuntivus is an open-source, self-hosted customer event delivery system for SaaS and platform teams. Publish business events once and define how each customer receives them.

:::warning Early development
Nuntivus is not yet production ready. APIs, schemas and configuration may change before the first stable release. Review the [verification scope and limitations](reference/features/product-verification.md) before evaluating it.
:::

## How it fits together

1. **Sources** receive or retrieve events.
2. **Events** carry a type, payload and metadata.
3. **Customer contracts** select events and define filtering, schemas, transformations and delivery policies.
4. **Destinations** receive deliveries over HTTP, Kafka, RabbitMQ, NATS or SQS.
5. **Operators** inspect delivery history, retries, replays, SLOs and alerts in the administration dashboard.

Processing is **at least once**. Downstream consumers must tolerate duplicate deliveries; Nuntivus does not promise exactly-once side effects in external systems.

## Explore

- [Try the local quick start](quick-start.md).
- [Understand the architecture](reference/architecture.md).
- [Configure managed Sources](reference/sources.md).
- [Review self-hosting operations](reference/self-hosting/README.md).
- [Read the source on GitHub](https://github.com/nuntivus/nuntivus-server).

The reference documentation is generated from this repository's `docs` directory at build time, so website and product documentation stay together. The project is distributed under the Apache-2.0 license; third-party dependencies retain their own licenses.
