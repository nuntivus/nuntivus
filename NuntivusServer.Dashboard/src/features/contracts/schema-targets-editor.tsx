import { Button, Card, Flex, Select, Text } from '@radix-ui/themes'
import { useId } from 'react'
import { FieldErrorText } from '../../components/forms/form-feedback'
import { useEventSchemaVersionsQuery } from '../event-definitions/use-event-definitions'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'

function SchemaTargetRow({ target, index, eventTypes, errors, update, remove, disabled }: {
  target: AdvancedOptionsDraft['schemaTargets'][number]
  index: number
  eventTypes: string[]
  errors: ValidationErrors
  update: (next: typeof target) => void
  remove: () => void
  disabled?: boolean
}) {
  const eventTypeId = useId()
  const versionId = useId()
  const versions = useEventSchemaVersionsQuery(target.eventType || undefined)
  const availableVersions = versions.data?.items ?? []
  const eventTypeError = errors[`schemaTargets.${index}.eventType`]
  const versionError = errors[`schemaTargets.${index}.version`]
  return <Card>
    <Flex direction="column" gap="2">
      <Flex gap="3" wrap="wrap" align="start">
        <Flex direction="column" gap="1" style={{ flex: '1 1 220px' }}>
          <Text id={`${eventTypeId}-label`} size="2" weight="medium">Event type {index + 1}</Text>
          <Select.Root value={target.eventType} onValueChange={(eventType) => update({ ...target, eventType, version: '' })} disabled={disabled || eventTypes.length === 0}>
            <Select.Trigger id={eventTypeId} placeholder={eventTypes.length ? 'Select event type' : 'No contract event types'} aria-labelledby={`${eventTypeId}-label`} aria-invalid={!!eventTypeError} aria-describedby={eventTypeError ? `${eventTypeId}-error` : undefined} />
            <Select.Content>
              {eventTypes.map((eventType) => <Select.Item key={eventType} value={eventType}>{eventType}</Select.Item>)}
            </Select.Content>
          </Select.Root>
          <FieldErrorText id={`${eventTypeId}-error`} message={eventTypeError} />
        </Flex>
        <Flex direction="column" gap="1" style={{ flex: '1 1 220px' }}>
          <Text id={`${versionId}-label`} size="2" weight="medium">Schema version</Text>
          <Select.Root value={target.version} onValueChange={(version) => update({ ...target, version })} disabled={disabled || !target.eventType || versions.isLoading || availableVersions.length === 0}>
            <Select.Trigger id={versionId} placeholder={versions.isLoading ? 'Loading versions...' : availableVersions.length ? 'Select schema version' : 'No schema versions'} aria-labelledby={`${versionId}-label`} aria-invalid={!!versionError} aria-describedby={versionError ? `${versionId}-error` : undefined} />
            <Select.Content>
              {availableVersions.map((version) => <Select.Item key={version.version} value={String(version.version)}>v{version.version} · {version.state}</Select.Item>)}
            </Select.Content>
          </Select.Root>
          <FieldErrorText id={`${versionId}-error`} message={versionError} />
        </Flex>
      </Flex>
      {versions.isError ? <Text size="1" color="amber">Could not load schema versions for this event type.</Text> : null}
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
    <Button type="button" variant="soft" onClick={() => onChange([...value, { eventType: eventTypes.length === 1 ? eventTypes[0] : '', version: '' }])} disabled={disabled} style={{ alignSelf: 'flex-start' }}>+ Add schema target</Button>
  </Flex>
}
