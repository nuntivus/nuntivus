import assert from 'node:assert/strict'
import { test } from 'node:test'
import { auditPreviewFields, parseAuditDetails } from '../src/features/audit-logs/audit-details.ts'

test('audit payload decodes nested request and response JSON without interpreting ordinary strings', () => {
  const payload = parseAuditDetails(JSON.stringify({
    request: JSON.stringify({ deliveryId: 'delivery-1', reason: 'Retry after recovery', identifiers: ['001', 'true'] }),
    response: JSON.stringify({ status: 'Pending', attemptCount: 0 }),
    message: 'not JSON', invalid: '{unfinished',
  }))
  assert.deepEqual(payload, {
    request: { deliveryId: 'delivery-1', reason: 'Retry after recovery', identifiers: ['001', 'true'] },
    response: { status: 'Pending', attemptCount: 0 }, message: 'not JSON', invalid: '{unfinished',
  })
})

test('audit previews show labeled values for nested SLO configuration instead of raw JSON', () => {
  const payload = parseAuditDetails('{"Request":{"LatencyTarget":99,"SuccessTarget":99.9,"SuccessDeadlineSeconds":86400,"LatencyThresholdSeconds":60}}')
  assert.deepEqual(auditPreviewFields(payload), [
    { label: 'Latency Target', value: '99' },
    { label: 'Success Target', value: '99.9' },
    { label: 'Success Deadline Seconds', value: '86400' },
    { label: 'Latency Threshold Seconds', value: '60' },
  ])
})

test('audit previews prioritize explanatory fields and preserve zero and false values', () => {
  assert.deepEqual(auditPreviewFields({ count: 0, reason: 'No matches', completed: false, filters: [], extra: 'more' }), [
    { label: 'Reason', value: 'No matches' }, { label: 'Count', value: '0' },
    { label: 'Completed', value: 'false' }, { label: 'Filters', value: '0 items' },
  ])
})

test('malformed and primitive audit payloads remain readable and previews are bounded', () => {
  assert.deepEqual(parseAuditDetails('not-json'), { raw: 'not-json' })
  assert.deepEqual(auditPreviewFields(parseAuditDetails('not-json')), [{ label: 'Raw', value: 'not-json' }])
  assert.deepEqual(auditPreviewFields(null), [{ label: 'Value', value: 'null' }])
  assert.deepEqual(auditPreviewFields({}), [])
  assert.deepEqual(auditPreviewFields([1, 2]), [{ label: 'Items', value: '2' }])
  const details = JSON.stringify({ message: 'x'.repeat(500), another: 'value' })
  assert.equal(auditPreviewFields(parseAuditDetails(details))[0].value.length, 121)
  assert.equal(parseAuditDetails(details).message.length, 500)
})

test('deeply encoded snapshots retain the original content beyond the normalization limit', () => {
  let value = { reason: 'Original reason' }
  for (let depth = 0; depth < 12; depth++) value = { snapshot: JSON.stringify(value) }
  const parsed = parseAuditDetails(JSON.stringify(value))
  assert.ok(JSON.stringify(parsed).includes('Original reason'))
})
