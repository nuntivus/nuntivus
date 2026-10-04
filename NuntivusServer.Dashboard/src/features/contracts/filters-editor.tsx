import { Badge, Button, Card, Flex, Popover, Select, Text, TextField } from '@radix-ui/themes'
import { useId, useState } from 'react'
import { FieldErrorText } from '../../components/forms/form-feedback'
import { SearchIcon } from '../../components/icons/nav-icons'
import { isSetOperator, operators, type AdvancedOptionsDraft, type FilterRuleDraft, type ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'
import { useSchemaFieldOptions, type SchemaFieldOption } from './use-schema-field-options'

type SchemaFieldNode = {
  name: string
  path: string
  field?: SchemaFieldOption
  children: Map<string, SchemaFieldNode>
}


function buildSchemaFieldTree(fields: SchemaFieldOption[]) {
  const roots = new Map<string, SchemaFieldNode>()

  fields.forEach((field) => {
    let siblings = roots
    let currentPath = ''
    field.path.split('.').forEach((name, index, parts) => {
      currentPath = currentPath ? `${currentPath}.${name}` : name
      const node = siblings.get(name) ?? {
        name,
        path: currentPath,
        children: new Map<string, SchemaFieldNode>(),
      }
      if (index === parts.length - 1) node.field = field
      siblings.set(name, node)
      siblings = node.children
    })
  })

  return [...roots.values()].sort((left, right) => left.name.localeCompare(right.name))
}

function SchemaFieldTreeNode({ node, depth, onSelect }: {
  node: SchemaFieldNode
  depth: number
  onSelect: (path: string) => void
}) {
  const children = [...node.children.values()].sort((left, right) => left.name.localeCompare(right.name))

  if (children.length === 0 && node.field) {
    return <Button type="button" variant="ghost" color="gray" className="schema-field-tree-leaf" onClick={() => onSelect(node.path)}>
      <span>{node.name}</span>
      <Text as="span" size="1" color="gray">{node.field.type} · {node.field.scope}</Text>
    </Button>
  }

  return <details className="schema-field-tree-branch" open={depth === 0}>
    <summary>
      <span>{node.name}</span>
      <Text as="span" size="1" color="gray">{children.length} {children.length === 1 ? 'field' : 'fields'}</Text>
    </summary>
    {node.field ? <Button type="button" size="1" variant="ghost" color="gray" onClick={() => onSelect(node.path)}>Use {node.path}</Button> : null}
    <div className="schema-field-tree-children">
      {children.map((child) => <SchemaFieldTreeNode key={child.path} node={child} depth={depth + 1} onSelect={onSelect} />)}
    </div>
  </details>
}

export function PayloadFieldPicker({ label, path, value, onChange, fields, hint, loading, loadError, errors, disabled }: {
  label: string
  path: string
  value: string
  onChange: (value: string) => void
  fields: SchemaFieldOption[]
  hint: string
  loading: boolean
  loadError: boolean
  errors: ValidationErrors
  disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const inputId = useId()
  const errorId = `${path.replace(/[^a-z0-9]/gi, '-')}-error`
  const tree = buildSchemaFieldTree(fields)
  const customPath = fields.length > 0 && value.trim() &&
    !fields.some((field) => field.path === value.trim())
  const selectField = (fieldPath: string) => {
    onChange(fieldPath)
    setOpen(false)
  }

  return <Flex direction="column" gap="1">
    <Text as="label" htmlFor={inputId} size="2" weight="medium">{label}</Text>
    <div className="payload-field-control">
      <TextField.Root id={inputId} value={value} onChange={(event) => onChange(event.target.value)} disabled={disabled} placeholder="customer.email or a custom path" aria-invalid={!!errors[path]} aria-describedby={errors[path] ? errorId : undefined} />
      <Popover.Root open={open} onOpenChange={setOpen}>
        <Popover.Trigger>
          <Button type="button" variant="soft" disabled={disabled}>
            <SearchIcon /> Browse schema
          </Button>
        </Popover.Trigger>
        <Popover.Content className="schema-field-browser" align="end">
          <Flex direction="column" gap="2">
            <Text size="2" weight="bold">Select a payload field</Text>
            <Text size="1" color="gray">Expand the JSON structure and choose a field. You can still type a custom path.</Text>
            <div className="schema-field-browser-scroll">
              {loading ? <Text size="2" color="gray">Loading schema fields...</Text> : null}
              {!loading && loadError ? <Text size="2" color="amber">Schema fields could not be loaded. You can enter a custom path.</Text> : null}
              {!loading && !loadError && tree.length === 0 ? <Text size="2" color="gray">No fields were found in the available event schemas.</Text> : null}
              {!loading ? tree.map((node) => <SchemaFieldTreeNode key={node.path} node={node} depth={0} onSelect={selectField} />) : null}
            </div>
          </Flex>
        </Popover.Content>
      </Popover.Root>
    </div>
    <Text size="1" color="gray">{hint}</Text>
    {customPath ? <Text size="1" color="amber">Custom path not found in the available schemas. It will still be saved.</Text> : null}
    <FieldErrorText id={errorId} message={errors[path]} />
  </Flex>
}

function FilterRuleEditor({ rule, index, scope, schemaFields, hasEventTypes, schemaFieldsLoading, schemaFieldsLoadError, errors, update, remove, disabled }: {
  rule: FilterRuleDraft
  index: number
  scope: 'metadataRules' | 'payloadRules'
  schemaFields: SchemaFieldOption[]
  hasEventTypes: boolean
  schemaFieldsLoading: boolean
  schemaFieldsLoadError: boolean
  errors: ValidationErrors
  update: (next: FilterRuleDraft) => void
  remove: () => void
  disabled?: boolean
}) {
  const [entry, setEntry] = useState('')
  const inputId = useId()
  const path = `filter.${scope}.${index}`
  const isPayloadRule = scope === 'payloadRules'
  const selectorHint = !isPayloadRule
    ? 'Dot-separated field path'
    : schemaFieldsLoading
      ? 'Loading fields from selected schemas...'
      : schemaFields.length > 0
        ? 'Choose a schema field or enter a custom dot-separated path.'
        : hasEventTypes
          ? 'No fields were found in the active event schemas. Enter a custom path.'
          : 'Add an event type for schema suggestions, or enter a custom path.'
  const addValue = () => { if (entry.trim()) { update({ ...rule, values: [...rule.values, entry.trim()] }); setEntry('') } }
  return <Card>
    <Flex direction="column" gap="2">
      <Flex gap="3" wrap="wrap" align="start">
        {isPayloadRule
          ? <PayloadFieldPicker label="Payload field" path={`${path}.selector`} value={rule.selector} onChange={(selector) => update({ ...rule, selector })} fields={schemaFields} hint={selectorHint} loading={schemaFieldsLoading} loadError={schemaFieldsLoadError} errors={errors} disabled={disabled} />
          : <OptionField label="Metadata field" path={`${path}.selector`} value={rule.selector} onChange={(selector) => update({ ...rule, selector })} errors={errors} disabled={disabled} hint={selectorHint} />}
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

function FilterRuleList({ title, scope, value, onChange, schemaFields, hasEventTypes, schemaFieldsLoading, schemaFieldsLoadError, errors, disabled }: {
  title: string
  scope: 'metadataRules' | 'payloadRules'
  value: FilterRuleDraft[]
  onChange: (next: FilterRuleDraft[]) => void
  schemaFields: SchemaFieldOption[]
  hasEventTypes: boolean
  schemaFieldsLoading: boolean
  schemaFieldsLoadError: boolean
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="2">
    <Text size="2" weight="bold">{title}</Text>
    {value.map((rule, index) => <FilterRuleEditor key={index} rule={rule} index={index} scope={scope} schemaFields={schemaFields} hasEventTypes={hasEventTypes} schemaFieldsLoading={schemaFieldsLoading} schemaFieldsLoadError={schemaFieldsLoadError} errors={errors} disabled={disabled} update={(next) => onChange(value.map((item, i) => i === index ? next : item))} remove={() => onChange(value.filter((_, i) => i !== index))} />)}
    {!value.length ? <Text size="2" color="gray">No {title.toLowerCase()}.</Text> : null}
    <FieldErrorText id={`${scope}-count-error`} message={errors[`filter.${scope}`]} />
    <Button type="button" variant="soft" disabled={disabled || value.length >= 50} onClick={() => onChange([...value, { selector: '', operator: 'Equals', value: '', values: [] }])} style={{ alignSelf: 'flex-start' }}>+ Add {scope === 'metadataRules' ? 'metadata' : 'payload'} rule</Button>
  </Flex>
}

export function FiltersEditor({ value, onChange, eventTypes, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['filter']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['filter']>) => void
  eventTypes: string[]
  errors: ValidationErrors
  disabled?: boolean
}) {
  const schemaFields = useSchemaFieldOptions(eventTypes)
  return <Flex direction="column" gap="4">
    <Text size="2" color="gray">All configured rules must match. Choose a field path, comparison, and value.</Text>
    {(['metadataRules', 'payloadRules'] as const).map((scope) => <FilterRuleList key={scope} title={scope === 'metadataRules' ? 'Metadata rules' : 'Payload rules'} scope={scope} value={value[scope]} onChange={(next) => onChange({ ...value, [scope]: next })} schemaFields={schemaFields.options} hasEventTypes={schemaFields.hasEventTypes} schemaFieldsLoading={schemaFields.isLoading} schemaFieldsLoadError={schemaFields.isError} errors={errors} disabled={disabled} />)}
  </Flex>
}
