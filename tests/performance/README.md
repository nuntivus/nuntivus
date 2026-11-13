# Docker connectivity and stress measurements

Run from the repository root with Docker Desktop in Linux-container mode, the configured .NET SDK, and Node.js available. These tests use Docker services and LocalStack, never real AWS. No credentials, admin tokens, or encryption keys belong in results or source control.

## Live connectivity

```powershell
 dotnet test NuntivusServer.IntegrationTests/NuntivusServer.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~LiveSourceConnectivityTests|FullyQualifiedName~NonHttpDeliveryExecutorsIntegrationTests|FullyQualifiedName~AdapterStressTests"
```

The Testcontainers collection starts Kafka 7.6.1, RabbitMQ 3.13, NATS 2.10 with JetStream, LocalStack 3.5 with SQS and S3, and a Python 3.12 HTTP receiver. Ports are mapped randomly. Fixtures/Ryuk dispose only their own containers. The existing local NATS service on port 4222 is not stopped or reused.

Coverage:

- RabbitMQ Source passive queue inspection preserves messages and consumers.
- Kafka Source inspects an existing topic, preserves its message, rejects missing topics, and does not auto-create them.
- NATS Source inspects JetStream configuration without consuming.
- SQS Source checks queue attributes and preserves a seeded message.
- S3 Source lists a prefix, preserves a seeded object, and rejects a missing bucket.
- HTTP polling Source uses a real HEAD probe; its runner stress case retrieves real HTTP responses.
- HTTP API Source validates configured credentials; the pipeline harness exercises authenticated live ingestion.
- Folder Source checks and reads real temporary files. A folder is a local-filesystem adapter, not a network connection.
- All five Destination types execute real HTTP requests or acknowledged broker publications, including their per-type connection testers.

`ISourceTransportClientFactory` is shared by Source probes and runners. The production factory retains AWS region endpoints, Kafka SSL/SASL_SSL, and private-network/redirect restrictions for HTTP polling. Only `DockerSourceClients` in the test assembly selects LocalStack URLs, plaintext local Kafka, and local HTTP. These tests do not demonstrate Kafka TLS/SASL, AWS IAM, or production certificate validation.

## Individual adapter stress

```powershell
$env:STRESS_SAMPLES = '1000'
$env:PERF_RESULTS_DIR = Join-Path (Get-Location) 'tests/performance/results/adapters-local'
dotnet test NuntivusServer.IntegrationTests/NuntivusServer.IntegrationTests.csproj -c Release --filter 'Category=AdapterStress' --logger 'trx;LogFileName=adapter-stress.trx'
Remove-Item Env:STRESS_SAMPLES, Env:PERF_RESULTS_DIR
```

Normal test runs use 10 samples per case. Explicit stress runs accept 1–100,000 samples, with a five-minute bound per scenario. Run this collection alone, not concurrently with other test suites, image builds, or pipeline measurements.

- Destinations: five warmup attempts, then bounded concurrency 1 and 8. Payload contains 1,024 data characters plus JSON framing. Latencies measure individual real executor attempts, including request construction/serialization and acknowledgement. Successful broker publishing does not mean a downstream business consumer processed the message; RabbitMQ confirms do not establish routing success.
- Sources: seven real runners, one worker each, using preseeded broker messages, files or S3 objects. Seeding is excluded. Cold connection/group startup is included. HTTP polling retrieves changing small JSON responses with zero interval to isolate adapter overhead; this is not a suggested production polling configuration. Source latency is **queue residence since runner start**, not individual network-operation latency. The processor parses JSON, records distinct stable keys in memory and returns success. These Source numbers exclude database persistence, schema validation, matching and delivery.
- Inbound HTTP has no pull runner. Its API acceptance throughput/latency is measured separately within each full-pipeline case below.
- Each case asserts exact successful sample counts and no reported errors. The tests do not inject faults, establish a saturation limit, or execute a multi-hour soak.
- JSON results and test output contain elapsed time, successful operations/second, errors, p50/p95/p99/max latency, test-process CPU time, total managed allocated bytes, GC collection deltas, working set and process peak working set. Process counters include framework/background work and the process peak is cumulative; they are not adapter-exclusive CPU or heap measurements. Source resource counters also include the short cancellation/drain tail.

## Full ingestion-to-delivery pipeline

```powershell
$env:PERF_EVENTS = '500'
$env:PERF_CONCURRENCY = '1,8,32'
node tests/performance/run-pipeline.mjs
Remove-Item Env:PERF_EVENTS, Env:PERF_CONCURRENCY
```

The coordinator creates a random Compose project, builds the actual API and Worker images, runs migrations, bootstraps an admin through stdin, and passes the login token only via the load generator environment. A random managed Source token authenticates ingestion. Twenty warmup events are delivered before measurements.

The path is:

`HTTP_API -> PostgreSQL event/outbox persistence -> NATS JetStream -> contract matcher -> delivery dispatcher -> real HTTP receiver -> Delivered persistence`.

The load generator runs inside the Docker network, not through repeated `docker exec` calls. Each configured concurrency submits a finite burst of events with 1,024 data characters plus envelope/framing. API latency uses a monotonic clock; end-to-end latency uses API request-start timestamps and receiver arrival timestamps on Docker's shared host clock. Throughput includes queue draining to the last unique receiver arrival, not just fast HTTP acceptance.

The receiver verifies event IDs, counts unique deliveries and duplicates, and reports end-to-end percentiles. The coordinator verifies exact persisted event and Delivered counts, including warmup. It records Docker CPU/memory samples during measured traffic and retains raw results under `results/pipeline-local/`. CPU values are Docker percentages, where 100% represents approximately one logical CPU; they may exceed 100%. Resource sampling is coarse (approximately every 2–3 seconds), not a profiler trace or an exact peak.

Cleanup runs in `finally` and removes only the randomly named project and its volumes. No host ports are published and no existing stack is reset. Generated results/logs are ignored by Git. This is a disposable test deployment, not a command for a production database.

The full-pipeline workload currently uses HTTP_API to HTTP. Non-HTTP adapters have independent Docker-backed stress measurements; all possible Source/Destination pipeline combinations are **not** covered.

## Measured local baseline

See [local-baseline.md](local-baseline.md) for the observed run conditions and measured results. Treat these as local finite-workload measurements, not production sizing guarantees or an optimized before/after comparison.
