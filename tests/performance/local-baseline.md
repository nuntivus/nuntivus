# Observed local Docker baseline

These are actual measurements from the harnesses in this directory, not estimated production capacity. No performance optimizations were made or claimed. Raw JSON/TRX files remain in the workspace under `results/` and the integration project's `TestResults/`; generated artifacts are ignored by Git.

## Conditions

- Run timestamps reported by the machine: 2026-11-13, approximately 10:28–10:31 UTC.
- Windows 11 build 26200; Intel Core i7-11800H, 8 physical cores / 16 logical processors.
- Docker Desktop 29.8.2, WSL2 kernel 6.6.87.2, reported Docker memory budget 15.47 GiB.
- Integration adapters: Release, .NET 8.0.31 on Windows, SDK 10.0.401.
- Pipeline API/Worker: actual Release Docker builds, Linux .NET 8.0.34. Load generator: Node 22 container.
- Kafka 7.6.1, RabbitMQ 3.13, NATS 2.10 JetStream, LocalStack 3.5 (SQS/S3). PostgreSQL 16 in the pipeline.
- No explicit CPU/memory container limits. Existing unrelated NATS on host port 4222 remained running, unused by these tests. Pipeline and final adapter stress runs were sequential, not concurrent.
- One observed run per workload, not BenchmarkDotNet statistical iterations. The earlier 100-sample smoke run is not used below. Very short cases are especially sensitive to scheduling, GC and timer noise.

## Full authenticated HTTP ingestion-to-HTTP delivery

Twenty warmup events, then 500 events per case; 1,024 data characters plus JSON envelope/framing. Default Worker settings, including matcher/delivery concurrency 8 and the configured outbox polling delays.

| Client concurrency | Accepted/s | Delivered/s including drain | API p50 / p95 / p99 ms | End-to-end p50 / p95 / p99 ms | Ingestion / total pipeline seconds |
| --- | ---: | ---: | --- | --- | --- |
| 1 | 171.4 | 85.9 | 5.43 / 9.16 / 10.60 | 2501 / 3051 / 3085 | 2.918 / 5.822 |
| 8 | 973.0 | 92.1 | 7.47 / 13.43 / 19.50 | 4018 / 4902 / 4914 | 0.514 / 5.426 |
| 32 | 1072.3 | 94.1 | 25.89 / 68.39 / 85.31 | 3938 / 4845 / 4855 | 0.466 / 5.314 |

All three cases accepted and delivered exactly 500 events, with **zero ingestion errors and zero observed duplicate receiver deliveries**. PostgreSQL verification found **1,520 events and 1,520 Delivered records**, including warmup; no other delivery statuses appeared.

Interpretation: faster API acceptance did not translate into proportional full-pipeline throughput. The burst accumulated in the asynchronous pipeline, with measured p95 delivery latency around 3.05–4.90 seconds. This establishes a queueing signal, not a profiler-confirmed root cause. Finite-burst throughput does not establish sustainable saturation capacity.

### Sampled Docker resources during pipeline traffic

| Service | Highest sampled CPU % | Highest sampled memory MiB |
| --- | ---: | ---: |
| API | 182.14 | 208.50 |
| Worker | 109.87 | 118.20 |
| PostgreSQL | 187.63 | 209.70 |
| NATS | 7.03 | 19.09 |
| Receiver | 26.14 | 35.77 |
| Load generator | 69.75 | 63.96 |

Eight resource samples were collected for each long-running service; six for the load generator. CPU percentages can exceed 100% on multicore machines. These are coarse sampled maxima, not exact peaks; sampling and the controlled receiver/load generator consume resources too. They do not demonstrate a leak or identify hot functions.

## Individual Destination executors

1,000 measured attempts per case, five warmup attempts excluded, 1,024 data characters plus JSON framing. HTTP measures receiver responses; Kafka, NATS JetStream, RabbitMQ and SQS measure successful broker acknowledgements/API responses, not downstream consumer processing.

| Destination | Concurrency | Successful attempts/s | p95 ms | p99 ms | Test-process CPU ms | Managed allocation MiB | Test-process working set MiB | GC 0/1/2 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| HTTP | 1 | 693.0 | 1.71 | 1.99 | 813 | 26.27 | 158.8 | 1/1/1 |
| HTTP | 8 | 989.6 | 2.83 | 5.93 | 953 | 26.39 | 142.8 | 1/1/1 |
| Kafka | 1 | 64.6 | 16.36 | 16.66 | 609 | 10.70 | 158.6 | 1/1/0 |
| Kafka | 8 | 529.8 | 15.90 | 16.99 | 266 | 10.85 | 121.5 | 1/0/0 |
| NATS JetStream | 1 | 1506.2 | 0.74 | 0.94 | 172 | 11.25 | 160.0 | 0/0/0 |
| NATS JetStream | 8 | 10079.8 | 0.96 | 1.52 | 31 | 11.77 | 158.8 | 1/0/0 |
| RabbitMQ | 1 | 387.3 | 3.00 | 3.50 | 672 | 10.30 | 157.4 | 0/0/0 |
| RabbitMQ | 8 | 2224.3 | 4.32 | 5.54 | 313 | 10.53 | 156.5 | 1/1/0 |
| SQS LocalStack | 1 | 425.5 | 2.47 | 3.09 | 859 | 72.75 | 152.6 | 2/1/0 |
| SQS LocalStack | 8 | 716.2 | 15.93 | 18.39 | 531 | 72.95 | 161.8 | 5/3/0 |

All ten cases completed exactly 1,000 successful attempts with zero reported errors. The NATS concurrency-8 case lasted only approximately 0.099 seconds; its high rate is a short-burst observation, not a validated sustained rate. RabbitMQ confirms do not establish routing success, and this workload does not assert downstream receipt for every broker publication.

## Individual Source runners

1,000 distinct items per case, one actual Source runner and an in-memory successful processor. Preseeding is excluded; cold connection/consumer-group startup is included. Folder tests run against Windows temporary files; S3/SQS use LocalStack. Polling responses are small generated JSON, unlike the approximately 1 KiB broker/file/object bodies.

**The following percentiles measure queue residence from runner start to processor callback, not single-operation latency. Do not compare them to Destination request latency or use them to rank network round trips.**

| Source | Items/s | Residence p95 ms | Residence p99 ms | Test-process CPU ms | Managed allocation MiB | Test-process working set MiB | GC 0/1/2 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Folder | 1892.1 | 502.95 | 523.68 | 406 | 14.01 | 153.6 | 1/1/0 |
| HTTP polling | 750.8 | 1267.43 | 1318.32 | 688 | 30.07 | 159.5 | 3/1/0 |
| Kafka | 459.4 | 2135.58 | 2168.17 | 188 | 4.73 | 159.2 | 0/0/0 |
| NATS | 1547.1 | 617.58 | 640.59 | 219 | 9.00 | 160.7 | 1/1/0 |
| RabbitMQ | 1539.5 | 613.40 | 642.83 | 141 | 5.31 | 153.9 | 1/0/0 |
| S3 LocalStack | 494.1 | 1929.34 | 2004.69 | 563 | 78.67 | 156.7 | 7/4/0 |
| SQS LocalStack | 271.4 | 3513.28 | 3650.93 | 672 | 102.82 | 158.1 | 9/5/0 |

All seven cases observed exactly 1,000 distinct successful callbacks with zero reported errors. Inbound HTTP_API has no pull runner; its measured acceptance rate and API latency are in the full-pipeline table. These runner measurements exclude the production ingestion processor, database, schemas, leases, matching and delivery. CPU/allocation/GC figures are process-wide deltas; Source resource figures include the short cancellation/drain tail. Working set is not exclusively adapter-owned memory, and raw JSON also records the cumulative process peak working set.

## Limits and next measurements

Validation completed: solution build succeeded, all 320 unit tests and 376 integration tests passed, all 17 explicit 1,000-sample adapter stress cases passed, and the full Docker pipeline verified 1,520 persisted Delivered events. Documentation link checking reported no missing local targets. The owned test containers and Compose volumes were removed; the unrelated pre-existing NATS container remained running. Frontend/browser suites were not rerun because no frontend code was changed.

- LocalStack does not model real AWS latency, quotas, IAM policies or all S3/SQS semantics.
- Local plaintext Kafka does not validate TLS, SASL or production certificate trust. Secure production Source defaults are retained and tested separately.
- Only HTTP_API-to-HTTP has full-pipeline measurements. Individual adapters do not prove every non-HTTP end-to-end combination.
- No fault injection, prolonged soak, production-sized backlog, saturation search or cross-machine network test was performed.
- No CPU/allocation profiler trace was collected, so resource counters alone must not be treated as a hotspot diagnosis.
- Before changing Worker throughput settings, profile the measured full path and rerun the same workloads. Increase sample counts/duration and use repeated runs for capacity planning.
