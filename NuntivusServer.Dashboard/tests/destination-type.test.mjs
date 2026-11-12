import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createDefaultDestinationFormState, mapDestinationFormToConfig, normalizeDestinationType } from '../src/features/contracts/destination-forms/types.ts'

const destinationTypes = [
  [1, 'Http'],
  [2, 'Kafka'],
  [3, 'Nats'],
  [4, 'RabbitMq'],
  [5, 'Sqs'],
]

test('backend destination type enums select the corresponding edit form', () => {
  for (const [number, name] of destinationTypes) {
    assert.equal(normalizeDestinationType(number), name)
    assert.equal(createDefaultDestinationFormState(normalizeDestinationType(number)).type, name)
    assert.equal(normalizeDestinationType(name), name)
    assert.equal(normalizeDestinationType(name.toLowerCase()), name)
  }
})

test('unknown destination types cannot be used to build an edit form', () => {
  assert.equal(normalizeDestinationType(0), null)
  assert.equal(normalizeDestinationType(6), null)
  assert.equal(normalizeDestinationType('unknown'), null)
})

test('a normalized existing HTTP destination can be serialized for update', () => {
  const state = createDefaultDestinationFormState(normalizeDestinationType(1))
  state.http.url = 'https://example.com/webhook'
  assert.deepEqual(mapDestinationFormToConfig(state), {
    type: 'http',
    url: 'https://example.com/webhook',
    method: 'POST',
    headers: {},
    timeoutSeconds: undefined,
    authenticationType: 'none',
    credential: undefined,
  })
})
