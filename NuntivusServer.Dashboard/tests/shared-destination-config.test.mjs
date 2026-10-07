import assert from 'node:assert/strict'
import { test } from 'node:test'
import { normalizeConfiguration } from '../src/features/contracts/destination-forms/normalize-configuration.ts'

test('shared destination editor normalizes stored configuration and preserves masked credentials', () => {
  const stored = {
    Url: 'https://hooks.example.com/one',
    Authentication: { Type: 'bearer', Credential: { Kind: 'ref', Value: '***' } },
    TimeoutSeconds: 30,
  }
  assert.deepEqual(normalizeConfiguration(stored), {
    url: 'https://hooks.example.com/one',
    authentication: { type: 'bearer', credential: { kind: 'ref', value: '***' } },
    timeoutSeconds: 30,
  })
})
