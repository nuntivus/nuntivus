import assert from 'node:assert/strict'
import { test } from 'node:test'
import { sourceConfiguration, sourceConfigurationFields, validatePollingJson } from '../src/features/sources/source-types.ts'

test('HTTP API source persists a selected ingestion method', () => {
  assert.deepEqual(sourceConfiguration('HTTP_API', 'none', '', 'PATCH', {}), {
    authentication: { type: 'none' }, method: 'PATCH',
  })
})

test('HTTP polling source round-trips structured headers and POST body', () => {
  const fields = { url: 'https://example.com/events', method: 'POST', headers: '{"X-Client":"test"}',
    body: '{"page":1}', intervalSeconds: '30' }
  validatePollingJson(fields)
  const configuration = sourceConfiguration('HTTP_POLLING', 'none', '', 'POST', fields)
  assert.deepEqual(configuration, { url: 'https://example.com/events', method: 'POST',
    headers: { 'X-Client': 'test' }, body: { page: 1 }, intervalSeconds: 30 })
  assert.deepEqual(sourceConfiguration('HTTP_POLLING', 'none', '', 'POST', sourceConfigurationFields(configuration)), configuration)
})

test('HTTP polling rejects unsafe JSON shapes before submission', () => {
  assert.throws(() => validatePollingJson({ method: 'GET', body: '{}' }), /requires POST/)
  assert.throws(() => validatePollingJson({ method: 'POST', headers: '[1,2]' }), /Headers must/)
})
