import { Card, Flex, Heading, Text } from '@radix-ui/themes'
import { ConfigSection } from './config-section'
import { FiltersEditor } from './filters-editor'
import { OrderingEditor } from './ordering-editor'
import { RateLimitEditor } from './rate-limit-editor'
import { SchemaTargetsEditor } from './schema-targets-editor'
import { PayloadTransformationEditor } from './transformation-editor'
import { validateAdvancedContractOptions, type AdvancedOptionsDraft } from './contract-options'

export function ContractOptionsForm({ value, onChange, eventTypes, disabled = false, onRawEditingChange }: {
  value: AdvancedOptionsDraft
  onChange: (next: AdvancedOptionsDraft) => void
  eventTypes: string[]
  disabled?: boolean
  onRawEditingChange?: (key: keyof AdvancedOptionsDraft, editing: boolean) => void
}) {
  const errors = validateAdvancedContractOptions(value, eventTypes)
  const common = { value, onChange, eventTypes, errors, disabled, onRawEditingChange }
  return <Card>
    <Flex direction="column" gap="3">
      <Heading size="4">Advanced contract options</Heading>
      <Text size="2" color="gray">Optional settings. Expand and enable only the sections you need. Each section also offers expert JSON editing.</Text>
      <ConfigSection {...common} title="Schema targets" sectionKey="schemaTargets" summary={`${value.schemaTargets.length} configured`} enabled={value.schemaTargets.length > 0} onToggle={(enabled) => onChange({ ...value, schemaTargets: enabled ? [{ eventType: eventTypes.length === 1 ? eventTypes[0] : '', version: '1' }] : [] })}>
        <SchemaTargetsEditor value={value.schemaTargets} onChange={(schemaTargets) => onChange({ ...value, schemaTargets })} eventTypes={eventTypes} errors={errors} disabled={disabled} />
      </ConfigSection>
      <ConfigSection {...common} title="Filters" sectionKey="filter" summary={`${(value.filter?.metadataRules.length ?? 0) + (value.filter?.payloadRules.length ?? 0)} rules`} enabled={value.filter !== null} onToggle={(enabled) => onChange({ ...value, filter: enabled ? { metadataRules: [], payloadRules: [] } : null })}>
        {value.filter ? <FiltersEditor value={value.filter} onChange={(filter) => onChange({ ...value, filter })} errors={errors} disabled={disabled} /> : null}
      </ConfigSection>
      <ConfigSection {...common} title="Payload transformation" sectionKey="transformation" summary={`${value.transformation?.fieldMappings.length ?? 0} mappings`} enabled={value.transformation !== null} onToggle={(enabled) => onChange({ ...value, transformation: enabled ? { version: '1', fieldMappings: [{ sourceSelector: '', targetSelector: '' }] } : null })}>
        {value.transformation ? <PayloadTransformationEditor value={value.transformation} onChange={(transformation) => onChange({ ...value, transformation })} errors={errors} disabled={disabled} /> : null}
      </ConfigSection>
      <ConfigSection {...common} title="Rate limiting" sectionKey="rateLimit" summary={value.rateLimit ? [value.rateLimit.perSecond && `${value.rateLimit.perSecond}/sec`, value.rateLimit.perMinute && `${value.rateLimit.perMinute}/min`].filter(Boolean).join(' · ') || 'Configure limits' : 'Off'} enabled={value.rateLimit !== null} onToggle={(enabled) => onChange({ ...value, rateLimit: enabled ? { perSecond: '', perMinute: '', burst: '' } : null })}>
        {value.rateLimit ? <RateLimitEditor value={value.rateLimit} onChange={(rateLimit) => onChange({ ...value, rateLimit })} errors={errors} disabled={disabled} /> : null}
      </ConfigSection>
      <ConfigSection {...common} title="Ordering" sectionKey="ordering" summary={value.ordering?.keySelector || (value.ordering ? 'Default key' : 'Off')} enabled={value.ordering !== null} onToggle={(enabled) => onChange({ ...value, ordering: enabled ? { keySelector: '' } : null })}>
        {value.ordering ? <OrderingEditor value={value.ordering} onChange={(ordering) => onChange({ ...value, ordering })} errors={errors} disabled={disabled} /> : null}
      </ConfigSection>
    </Flex>
  </Card>
}