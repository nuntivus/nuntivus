import { Badge, Button, Card, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { useId, useState } from 'react'
import { FieldErrorText } from '../../components/forms/form-feedback'
import { isSetOperator, operators, type AdvancedOptionsDraft, type FilterRuleDraft, type ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'

function FilterRuleEditor({ rule, index, scope, errors, update, remove, disabled }: {
  rule: FilterRuleDraft
  index: number
  scope: 'metadataRules' | 'payloadRules'
  errors: ValidationErrors
  update: (next: FilterRuleDraft) => void
  remove: () => void
  disabled?: boolean
}) {
  const [entry, setEntry] = useState('')
  const inputId = useId()
  const path = `filter.${scope}.${index}`
  const addValue = () => { if (entry.trim()) { update({ ...rule, values: [...rule.values, entry.trim()] }); setEntry('') } }
  return <Card>
    <Flex direction="column" gap="2">
      <Flex gap="3" wrap="wrap" align="start">
        <OptionField label={`Selector ${index + 1}`} path={`${path}.selector`} value={rule.selector} onChange={(selector) => update({ ...rule, selector })} errors={errors} disabled={disabled} hint="Dot-separated field path" />
        <Flex direction="column" gap="1" style={{ flex: '1 1 150px' }}>
          <Text as="label" size="2" weight="medium" htmlFor={inputId}>Operator</Text>
          <Select.Root value={rule.operator} onValueChange={(operator) => update({ ...rule, operator: operator as FilterRuleDraft['operator'], value: '', values: [] })} disabled={disabled}>
            <Select.Trigger id={inputId} aria-label={`Operator for ${scope} rule ${index + 1}`} aria-invalid={!!errors[`${path}.operator`]} />
            <Select.Content>{operators.map((operator) => <Select.Item key={operator} value={operator}>{operator}</Select.Item>)}</Select.Content>
          </Select.Root>
          <FieldErrorText id={`${inputId}-error`} message={errors[`${path}.operator`]} />
        </Flex>
      </Flex>
      {isSetOperator(rule.operator) ? <Flex direction="column" gap="2">
        <Text as="label" htmlFor={`${inputId}-value`} size="2" weight="medium">Values</Text>
        <Flex gap="2" align="center">
          <TextField.Root id={`${inputId}-value`} value={entry} onChange={(event) => setEntry(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); addValue() } }} aria-invalid={!!errors[`${path}.values`]} aria-describedby={errors[`${path}.values`] ? `${inputId}-values-error` : undefined} disabled={disabled} placeholder="Type a value and press Enter" />
          <Button type="button" variant="soft" disabled={disabled || !entry.trim()} onClick={addValue}>Add value</Button>
        </Flex>
        <Flex gap="2" wrap="wrap">{rule.values.map((value, i) => <Button key={i} type="button" size="1" variant="soft" color="gray" disabled={disabled} aria-label={`Remove value ${value}`} onClick={() => update({ ...rule, values: rule.values.filter((_, n) => n !== i) })}><Badge color="gray">{value} ×</Badge></Button>)}</Flex>
        <FieldErrorText id={`${inputId}-values-error`} message={errors[`${path}.values`]} />
      </Flex> : <OptionField label="Value" path={`${path}.value`} value={rule.value} onChange={(value) => update({ ...rule, value })} errors={errors} disabled={disabled} />}
      <Button type="button" variant="soft" color="red" size="1" disabled={disabled} onClick={remove} aria-label={`Remove ${scope === 'metadataRules' ? 'metadata' : 'payload'} rule ${index + 1}`} style={{ alignSelf: 'flex-end' }}>Remove rule</Button>
    </Flex>
  </Card>
}

function FilterRuleList({ title, scope, value, onChange, errors, disabled }: {
  title: string
  scope: 'metadataRules' | 'payloadRules'
  value: FilterRuleDraft[]
  onChange: (next: FilterRuleDraft[]) => void
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="2">
    <Text size="2" weight="bold">{title}</Text>
    {value.map((rule, index) => <FilterRuleEditor key={index} rule={rule} index={index} scope={scope} errors={errors} disabled={disabled} update={(next) => onChange(value.map((item, i) => i === index ? next : item))} remove={() => onChange(value.filter((_, i) => i !== index))} />)}
    {!value.length ? <Text size="2" color="gray">No {title.toLowerCase()}.</Text> : null}
    <FieldErrorText id={`${scope}-count-error`} message={errors[`filter.${scope}`]} />
    <Button type="button" variant="soft" disabled={disabled || value.length >= 50} onClick={() => onChange([...value, { selector: '', operator: 'Equals', value: '', values: [] }])} style={{ alignSelf: 'flex-start' }}>+ Add {scope === 'metadataRules' ? 'metadata' : 'payload'} rule</Button>
  </Flex>
}

export function FiltersEditor({ value, onChange, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['filter']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['filter']>) => void
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="4">
    <Text size="2" color="gray">All configured rules must match. Choose a field path, comparison, and value.</Text>
    {(['metadataRules', 'payloadRules'] as const).map((scope) => <FilterRuleList key={scope} title={scope === 'metadataRules' ? 'Metadata rules' : 'Payload rules'} scope={scope} value={value[scope]} onChange={(next) => onChange({ ...value, [scope]: next })} errors={errors} disabled={disabled} />)}
  </Flex>
}
