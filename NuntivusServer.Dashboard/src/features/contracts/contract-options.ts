import type {
  ContractFilterOperator, ContractOptions, ContractSchemaTarget,
} from '../../services/api/contracts-api'

export type OptionKey = keyof ContractOptions
export const optionKeys: OptionKey[] = ['schemaTargets', 'filter', 'transformation', 'rateLimit', 'ordering']
export const operators: ContractFilterOperator[] = [
  'Equals', 'NotEquals', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual',
  'Contains', 'StartsWith', 'EndsWith', 'In', 'NotIn',
]
export const isSetOperator = (operator: ContractFilterOperator) => operator === 'In' || operator === 'NotIn'

export interface FilterRuleDraft {
  selector: string
  operator: ContractFilterOperator
  value: string
  values: string[]
  [key: string]: unknown
}
export interface AdvancedOptionsDraft {
  schemaTargets: Array<Omit<ContractSchemaTarget, 'version'> & { version: string }>
  filter: { metadataRules: FilterRuleDraft[]; payloadRules: FilterRuleDraft[]; [key: string]: unknown } | null
  transformation: { version: string; fieldMappings: { sourceSelector: string; targetSelector: string; [key: string]: unknown }[]; [key: string]: unknown } | null
  rateLimit: { perSecond: string; perMinute: string; burst: string; [key: string]: unknown } | null
  ordering: { keySelector: string; [key: string]: unknown } | null
}

export type ValidationErrors = Record<string, string>
const selectorPattern = /^[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)*$/
const eventPattern = /^[a-z0-9][a-z0-9_-]*(\.[a-z0-9][a-z0-9_-]*)*$/
const positive = (value: string, max = Number.MAX_SAFE_INTEGER) =>
  /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) > 0 && Number(value) <= max
const selectorError = (value: string) =>
  !value.trim() ? 'A field path is required.' :
    value.length > 200 || !selectorPattern.test(value) ? 'Use dot-separated keys (up to 200 characters).' : null

export function backendConfigToFormState(options: ContractOptions = {}): AdvancedOptionsDraft {
  const rule = (item: FilterRuleDraft): FilterRuleDraft => ({ ...item, value: item.value ?? '', values: [...(item.values ?? [])] })
  return {
    schemaTargets: (options.schemaTargets ?? []).map((item) => ({ ...item, version: String(item.version) })),
    filter: options.filter ? {
      ...options.filter,
      metadataRules: (options.filter.metadataRules ?? []).map((item) => rule(item as FilterRuleDraft)),
      payloadRules: (options.filter.payloadRules ?? []).map((item) => rule(item as FilterRuleDraft)),
    } : null,
    transformation: options.transformation ? {
      ...options.transformation, version: String(options.transformation.version),
      fieldMappings: (options.transformation.fieldMappings ?? []).map((item) => ({ ...item })),
    } : null,
    rateLimit: options.rateLimit ? {
      ...options.rateLimit,
      perSecond: options.rateLimit.perSecond == null ? '' : String(options.rateLimit.perSecond),
      perMinute: options.rateLimit.perMinute == null ? '' : String(options.rateLimit.perMinute),
      burst: options.rateLimit.burst == null ? '' : String(options.rateLimit.burst),
    } : null,
    ordering: options.ordering ? { ...options.ordering, keySelector: options.ordering.keySelector ?? '' } : null,
  }
}

export function validateAdvancedContractOptions(state: AdvancedOptionsDraft, eventTypes: string[] = []): ValidationErrors {
  const errors: ValidationErrors = {}
  const set = (path: string, error: string | null) => { if (error) errors[path] = error }
  const seenEvents = new Set<string>()
  state.schemaTargets.forEach((target, i) => {
    const path = `schemaTargets.${i}`
    set(`${path}.eventType`, !target.eventType ? 'Event type is required.' :
      target.eventType.length > 128 || !eventPattern.test(target.eventType) ? 'Use a lowercase dotted event type (up to 128 characters).' :
        eventTypes.length && !eventTypes.includes(target.eventType) ? 'Event type must be in this contract.' :
          seenEvents.has(target.eventType) ? 'Each event type can have only one schema target.' : null)
    seenEvents.add(target.eventType)
    set(`${path}.version`, positive(target.version) ? null : 'Version must be a positive integer.')
  })
  if (state.filter) {
    for (const scope of ['metadataRules', 'payloadRules'] as const) {
      const rules = state.filter[scope]
      set(`filter.${scope}`, rules.length > 50 ? 'At most 50 rules are allowed.' : null)
      rules.forEach((rule, i) => {
        const path = `filter.${scope}.${i}`
        set(`${path}.selector`, selectorError(rule.selector))
        set(`${path}.operator`, operators.includes(rule.operator) ? null : 'Unsupported operator.')
        if (isSetOperator(rule.operator)) {
          set(`${path}.values`, !rule.values.length ? 'Add at least one value.' :
            rule.values.some((v) => !v.trim() || v.length > 500) ? 'Values must be non-empty and at most 500 characters.' : null)
        } else {
          set(`${path}.value`, !rule.value.trim() || rule.value.length > 500 ? 'Enter a value (up to 500 characters).' : null)
          if (['Contains', 'StartsWith', 'EndsWith'].includes(rule.operator)) {
            set(`${path}.values`, rule.values.length ? 'This operator does not accept a list of values. Remove it in JSON mode.' : null)
          }
        }
      })
    }
  }
  if (state.transformation) {
    set('transformation.version', positive(state.transformation.version) ? null : 'Version must be a positive integer.')
    const mappings = state.transformation.fieldMappings
    set('transformation.fieldMappings', !mappings.length ? 'Add at least one mapping.' :
      mappings.length > 100 ? 'At most 100 mappings are allowed.' : null)
    const seenTargets = new Set<string>()
    mappings.forEach((mapping, i) => {
      const path = `transformation.fieldMappings.${i}`
      set(`${path}.sourceSelector`, selectorError(mapping.sourceSelector))
      set(`${path}.targetSelector`, selectorError(mapping.targetSelector) ??
        (seenTargets.has(mapping.targetSelector) ? 'This target is already mapped.' : null))
      seenTargets.add(mapping.targetSelector)
    })
  }
  if (state.rateLimit) {
    const { perSecond, perMinute, burst } = state.rateLimit
    set('rateLimit.perSecond', perSecond && !positive(perSecond, 1_000_000) ? 'Use an integer from 1 to 1,000,000.' : null)
    set('rateLimit.perMinute', perMinute && !positive(perMinute, 60_000_000) ? 'Use an integer from 1 to 60,000,000.' : null)
    set('rateLimit.burst', burst && !positive(burst, 10_000_000) ? 'Use an integer from 1 to 10,000,000.' : null)
    set('rateLimit', !perSecond && !perMinute ? 'Set at least one rate limit.' : null)
  }
  if (state.ordering) set('ordering.keySelector', state.ordering.keySelector ? selectorError(state.ordering.keySelector) : null)
  return errors
}

export function formStateToBackendConfig(state: AdvancedOptionsDraft): ContractOptions {
  const rule = (item: FilterRuleDraft) => {
    const { value, values, ...rest } = item
    return isSetOperator(item.operator)
      ? { ...rest, ...(value ? { value } : {}), values }
      : { ...rest, value, ...(values.length ? { values } : {}) }
  }
  return {
    schemaTargets: state.schemaTargets.map((item) => ({ ...item, version: Number(item.version) })),
    filter: state.filter ? {
      ...state.filter,
      metadataRules: state.filter.metadataRules.map(rule),
      payloadRules: state.filter.payloadRules.map(rule),
    } : null,
    transformation: state.transformation ? {
      ...state.transformation, version: Number(state.transformation.version),
    } : null,
    rateLimit: state.rateLimit ? {
      ...state.rateLimit,
      perSecond: state.rateLimit.perSecond ? Number(state.rateLimit.perSecond) : null,
      perMinute: state.rateLimit.perMinute ? Number(state.rateLimit.perMinute) : null,
      burst: state.rateLimit.burst ? Number(state.rateLimit.burst) : null,
    } : null,
    ordering: state.ordering ? { ...state.ordering, keySelector: state.ordering.keySelector || null } : null,
  }
}

export function parseRawSection(key: OptionKey, text: string, state: AdvancedOptionsDraft, eventTypes: string[] = []) {
  let parsed: unknown
  try { parsed = JSON.parse(text) } catch { return { error: 'Invalid JSON. Check the syntax before switching to visual mode.' } }
  if (key === 'schemaTargets' ? !Array.isArray(parsed) : !parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return { error: `${key} must be ${key === 'schemaTargets' ? 'an array' : 'an object'}.` }
  }
  const object = parsed as Record<string, unknown>
  if (key === 'filter' && (!Array.isArray(object.metadataRules) || !Array.isArray(object.payloadRules)) ||
    key === 'transformation' && !Array.isArray(object.fieldMappings) ||
    key === 'schemaTargets' && (parsed as unknown[]).some((item) => !item || typeof item !== 'object' || Array.isArray(item) || typeof (item as Record<string, unknown>).eventType !== 'string') ||
    key === 'filter' && [...(object.metadataRules as unknown[]), ...(object.payloadRules as unknown[])].some((item) => !item || typeof item !== 'object' || Array.isArray(item) || typeof (item as Record<string, unknown>).selector !== 'string' || typeof (item as Record<string, unknown>).operator !== 'string' || (item as Record<string, unknown>).values != null && !Array.isArray((item as Record<string, unknown>).values)) ||
    key === 'transformation' && (object.fieldMappings as unknown[]).some((item) => !item || typeof item !== 'object' || Array.isArray(item) || typeof (item as Record<string, unknown>).sourceSelector !== 'string' || typeof (item as Record<string, unknown>).targetSelector !== 'string') ||
    key === 'ordering' && object.keySelector != null && typeof object.keySelector !== 'string') {
    return { error: `${key}: required rule or mapping arrays are missing.` }
  }
  if (key === 'filter' && [...(object.metadataRules as FilterRuleDraft[]), ...(object.payloadRules as FilterRuleDraft[])].some((rule) =>
    isSetOperator(rule.operator) ? !Array.isArray(rule.values) || rule.values.some((entry) => typeof entry !== 'string') : typeof rule.value !== 'string')) {
    return { error: 'filter: each rule needs a string value or an array of string values for In / NotIn.' }
  }
  if (key === 'rateLimit' && ['perSecond', 'perMinute', 'burst'].some((field) => object[field] != null && typeof object[field] !== 'number') ||
    key === 'transformation' && typeof object.version !== 'number' ||
    key === 'schemaTargets' && (parsed as ContractSchemaTarget[]).some((target) => typeof target.version !== 'number')) {
    return { error: `${key}: numeric fields must be numbers.` }
  }
  if (key === 'filter' && [...(object.metadataRules as FilterRuleDraft[]), ...(object.payloadRules as FilterRuleDraft[])].some((rule) =>
    rule.values?.some((entry) => typeof entry !== 'string'))) {
    return { error: 'filter: values must contain strings.' }
  }
  const next = backendConfigToFormState({ ...formStateToBackendConfig(state), [key]: parsed })
  const errors = validateAdvancedContractOptions(next, eventTypes)
  const sectionError = Object.entries(errors).find(([path]) => path === key || path.startsWith(`${key}.`))
  return sectionError ? { error: `${sectionError[0]}: ${sectionError[1]}` } : { state: next }
}