# Delivery Ordering Guarantee

Kairo delivery processing is at-least-once for all contracts.

For contracts with ordering enabled:

- Ordering is FIFO per `ordering_key`.
- Current implementation derives the key per contract (`contract:{contractId}`), with stored selector config reserved for future partition/custom key support.
- Only one delivery per ordered key is allowed to execute at a time.
- If an earlier delivery is pending, in-flight, or failed with retry scheduled, later deliveries for the same key are blocked.
- Retryable failures keep the lane blocked until the same delivery is retried/resolved.
- Terminal outcomes (`Delivered` or `Dead`) unblock the lane for subsequent deliveries.
- Ordering coordinator state is persisted in `contract_delivery_ordering_state` and recovered after worker restarts via lease expiration.
- Deliveries for different contracts/keys continue concurrently.
