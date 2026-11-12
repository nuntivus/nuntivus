# Source and destination connection tests

Tests use the current form configuration, including unsaved edits and configured managed credentials. Stored destination credentials are resolved server-side; secret values and remote response bodies are not included in test feedback.

## Destinations

Every destination test performs a real send using its delivery executor: HTTP requests, Kafka publication, NATS JetStream publication, RabbitMQ publication, or SQS SendMessage. The dashboard requires confirmation because downstream receivers can process the test and trigger actions.

An editable JSON payload is available for all destination types (maximum 64 KiB). HTTP GET sends no body. Tests honor each executor's configured routing, authentication, headers, timeout and acknowledgement semantics. The API does not enqueue a Nuntivus event/delivery or schedule a delivery retry. Protocol clients retain their normal transport behavior.

HTTP success requires a successful status from the configured method; HEAD reachability is no longer used for destination tests.

## Sources: non-consuming checks

Source tests must not consume source data. They do not fetch/acknowledge broker messages, advance Kafka offsets, create NATS consumers, delete SQS messages, or read/move/delete files or objects.

- RabbitMQ: authenticate and passively inspect the configured queue.
- Kafka: authenticate using the source's TLS/SASL settings and inspect existing topic metadata without joining a consumer group or auto-creating topics.
- NATS: authenticate and request configured JetStream stream information without creating a consumer.
- SQS: request configured queue attributes.
- S3: list at most one entry under the configured bucket/prefix, without reading its contents.
- Folder: enumerate JSON filenames on the API host without opening their contents. Worker-host access is not verified.
- HTTP polling: send HEAD with configured plain headers and bearer credentials. No fallback to GET/POST occurs; the configured polling method and retrieval permissions cannot be verified without fetching data. An endpoint rejecting HEAD does not pass this check.
- Inbound HTTP: validate normalized configuration and resolve any configured managed token without ingesting an event. Public ingress reachability and end-to-end authentication are not verified.

Metadata access does not prove consume/read permissions or end-to-end ingestion. Test messages explicitly describe these limits.
