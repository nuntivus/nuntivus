---
slug: delivery-is-more-than-a-request
title: Event delivery is more than an HTTP request
authors: [nuntivus]
tags: [architecture, reliability]
description: Why customer-specific routing, retries and visibility belong in an explicit delivery layer.
---

Sending a single request is straightforward. Delivering customer events repeatedly, with clear failure semantics and enough information to diagnose problems, is a different engineering problem.

<!-- truncate -->

## Separate acceptance from delivery

An ingestion response and a successful downstream delivery are different facts. Nuntivus separates the API from asynchronous Worker processing, with PostgreSQL and NATS JetStream supporting its event flow. Operators can inspect what happened after acceptance instead of treating a network call as the entire lifecycle.

## Make contracts visible

A customer contract captures event selection, delivery configuration and optional policies. Schema review, filtering and transformations make those choices explicit. Dry runs let operators inspect the pipeline without sending a delivery.

## Design for duplicates

Timeouts create ambiguity: a remote service may have accepted a request even when its response was lost. Retrying cannot remove that ambiguity. Consumers should use stable event identity and their own idempotency strategy to prevent duplicate business effects.

## Monitor promises, not assumptions

Success and latency SLOs describe observed delivery behavior. A monitoring target is not a switch that makes the network faster, and an empty traffic window is not proof of perfect reliability. Alerts and history help operators respond with context.

Read the [ordering guarantees](/docs/reference/delivery-ordering-guarantees), [contract SLOs](/docs/reference/features/contract-slos) and [verification limitations](/docs/reference/features/product-verification) for the project's actual boundaries.
