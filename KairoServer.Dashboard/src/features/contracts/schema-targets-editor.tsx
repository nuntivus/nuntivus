import { Button, Card, Flex, Text } from '@radix-ui/themes'
import { useId } from 'react'
import { useEventSchemaVersionQuery, useEventSchemaVersionsQuery } from '../event-definitions/use-event-definitions'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'

function SchemaTargetRow({ target, index, eventTypes, errors, update, remove, disabled }: {
  target: AdvancedOptionsDraft['schemaTargets'][number]
  index: number
  eventTypes: string[]
  errors: ValidationErrors
  update: (next: typeof target) => void
  remove: () => void
  disabled?: boolean
}) {
  const listId = useId()
  const versionsId = useId()
  const versions = useEventSchemaVersionsQuery(target.eventType || undefined)
  const availableVersions = versions.data?.items ?? []
  const selectedVersion = Number(target.version)
  const versionCheck = useEventSchemaVersionQuery(
    target.eventType || undefined,
    Number.isInteger(selectedVersion) && selectedVersion > 0 ? selectedVersion : undefined,
  )
  const versionNotFound = versionCheck.error && typeof versionCheck.error === 'object' &&
    'status' in versionCheck.error && versionCheck.error.status === 404
  return <Card>
    <Flex direction="column" gap="2">
      <Flex gap="3" wrap="wrap" align="start">
        <OptionField label={`Event type ${index + 1}`} path={`schemaTargets.${index}.eventType`} value={target.eventType} onChange={(eventType) => update({ ...target, eventType, version: eventType === target.eventType ? target.version : '' })} list={listId} errors={errors} disabled={disabled} />
        <OptionField label="Schema version" path={`schemaTargets.${index}.version`} type="number" min={1} value={target.version} onChange={(version) => update({ ...target, version })} list={versionsId} errors={errors} disabled={disabled} />
      </Flex>
      <datalist id={listId}>{eventTypes.map((eventType) => <option key={eventType} value={eventType} />)}</datalist>
      <datalist id={versionsId}>{availableVersions.map((version) => <option key={version.version} value={version.version} />)}</datalist>
      {versionNotFound ? <Text size="1" color="red" role="alert">This schema version does not exist for the selected event type.</Text> : null}
      {versionCheck.isError && !versionNotFound ? <Text size="1" color="amber">Could not verify this schema version. The API will check it when saved.</Text> : null}
      <Button type="button" variant="soft" color="red" size="1" onClick={remove} disabled={disabled} aria-label={`Remove schema target ${index + 1}`} style={{ alignSelf: 'flex-end' }}>Remove</Button>
    </Flex>
  </Card>
}

export function SchemaTargetsEditor({ value, onChange, eventTypes, errors, disabled }: {
  value: AdvancedOptionsDraft['schemaTargets']
  onChange: (next: AdvancedOptionsDraft['schemaTargets']) => void
  eventTypes: string[]
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="2">
    {value.map((target, index) => <SchemaTargetRow key={index} target={target} index={index} eventTypes={eventTypes} errors={errors} update={(next) => onChange(value.map((item, i) => i === index ? next : item))} remove={() => onChange(value.filter((_, i) => i !== index))} disabled={disabled} />)}
    {!value.length ? <Text size="2" color="gray">No schema targets. Add one to select a version for an event type.</Text> : null}
    <Button type="button" variant="soft" onClick={() => onChange([...value, { eventType: eventTypes.length === 1 ? eventTypes[0] : '', version: '1' }])} disabled={disabled} style={{ alignSelf: 'flex-start' }}>+ Add schema target</Button>
  </Flex>
}
