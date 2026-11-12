import assert from 'node:assert/strict'
import { test } from 'node:test'
import { backendConfigToFormState, formStateToBackendConfig, parseRawSection, validateAdvancedContractOptions } from '../src/features/contracts/contract-options.ts'

const sample = {
  schemaTargets: [{ eventType: 'orders.created', version: 2 }],
  filter: { metadataRules: [{ selector: 'tenant', operator: 'In', values: ['a', 'b'] }], payloadRules: [{ selector: 'order.id', operator: 'Equals', value: '10' }] },
  transformation: { version: 1, fieldMappings: [{ sourceSelector: 'order.id', targetSelector: 'payload.id' }] },
  rateLimit: { perSecond: 10, perMinute: null, burst: 20 },
  ordering: { keySelector: 'order.id' },
}

test('API hydration and form serialization preserve options and optional fields', () => {
  const state = backendConfigToFormState(sample)
  assert.deepEqual(validateAdvancedContractOptions(state, ['orders.created']), {})
  assert.deepEqual(formStateToBackendConfig(state), sample)
  const custom = backendConfigToFormState({ ordering: { keySelector: null, futureSetting: true } })
  assert.equal(formStateToBackendConfig(custom).ordering.futureSetting, true)
})

test('visual JSON round trip preserves section values; invalid JSON cannot replace state', () => {
  const state = backendConfigToFormState(sample)
  const text = JSON.stringify(formStateToBackendConfig(state).transformation)
  assert.deepEqual(parseRawSection('transformation', text, state).state?.transformation, state.transformation)
  assert.ok(parseRawSection('transformation', '{oops', state).error)
  assert.ok(parseRawSection('transformation', '{"version":1,"fieldMappings":[null]}', state).error)
  assert.deepEqual(state.transformation.fieldMappings[0], { sourceSelector: 'order.id', targetSelector: 'payload.id' })
})

test('operator-dependent filter validation and add/remove rule state', () => {
  const state = backendConfigToFormState()
  state.filter = { metadataRules: [{ selector: 'tenant', operator: 'In', value: '', values: [] }], payloadRules: [] }
  assert.equal(validateAdvancedContractOptions(state)['filter.metadataRules.0.values'], 'Add at least one value.')
  state.filter.metadataRules[0].values.push('a')
  assert.deepEqual(validateAdvancedContractOptions(state), {})
  state.filter.payloadRules.push({ selector: 'order.id', operator: 'Equals', value: '', values: [] })
  assert.ok(validateAdvancedContractOptions(state)['filter.payloadRules.0.value'])
  state.filter.payloadRules.pop()
  assert.deepEqual(validateAdvancedContractOptions(state), {})
})

test('invalid rate limits, duplicate targets and schema versions are rejected', () => {
  const state = backendConfigToFormState(sample)
  state.schemaTargets.push({ eventType: 'orders.created', version: '0' })
  state.rateLimit = { perSecond: '-1', perMinute: '', burst: '10000001' }
  const errors = validateAdvancedContractOptions(state, ['orders.created'])
  assert.ok(errors['schemaTargets.1.version'])
  assert.ok(errors['schemaTargets.1.eventType'])
  assert.ok(errors['rateLimit.perSecond'])
  assert.ok(errors['rateLimit.burst'])
})

test('add/remove mappings, duplicate targets, ordering and unknown section fields', () => {
  const state = backendConfigToFormState(sample)
  state.transformation.fieldMappings.push({ sourceSelector: 'item.id', targetSelector: 'payload.id' })
  assert.ok(validateAdvancedContractOptions(state)['transformation.fieldMappings.1.targetSelector'])
  state.transformation.fieldMappings.pop()
  assert.deepEqual(validateAdvancedContractOptions(state), {})
  const result = parseRawSection('ordering', '{"keySelector":"customer.id","futureField":"preserved"}', state)
  assert.equal(formStateToBackendConfig(result.state).ordering.futureField, 'preserved')
})

test('typed expression configuration survives visual and JSON round trips', () => {
  const config = { transformation: { version: 2, fieldMappings: [], expression: {
    op: 'default', value: { op: 'select', path: '/label' }, fallback: { op: 'constant', value: 'unknown' },
  } } }
  const state = backendConfigToFormState(config)
  assert.deepEqual(validateAdvancedContractOptions(state), {})
  assert.deepEqual(formStateToBackendConfig(state).transformation, config.transformation)
  assert.deepEqual(parseRawSection('transformation', JSON.stringify(config.transformation), state).state.transformation, state.transformation)
  state.transformation.expression = null
  assert.ok(validateAdvancedContractOptions(state)['transformation.expression'])
  state.transformation.fieldMappings.push({ sourceSelector: 'id', targetSelector: 'id' })
  assert.ok(validateAdvancedContractOptions(state)['transformation.fieldMappings'])
})
