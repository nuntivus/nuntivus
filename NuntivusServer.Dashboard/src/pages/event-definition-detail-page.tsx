import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  ScrollArea,
  Select,
  Table,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { CalendarIcon, CheckCircleIcon, ClockIcon, CodeIcon, ContractsIcon, CopyIcon, EventDefinitionsIcon, EventsIcon, PencilIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailMetric, DetailSectionTitle } from '../components/states/detail-visuals'
import {
  useActivateSchemaVersionMutation,
  useCreateSchemaDraftMutation,
  useDeprecateSchemaVersionMutation,
  useEventDefinitionDetailQuery,
  useEventSchemaVersionQuery,
  useEventSchemaVersionsQuery,
  useRelatedContractsQuery,
  useUpdateEventDefinitionMutation,
} from '../features/event-definitions/use-event-definitions'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

function mapSchemaStateColor(state: string): 'gray' | 'green' | 'amber' {
  const normalized = state.toLowerCase()
  if (normalized === 'active') {
    return 'green'
  }

  if (normalized === 'deprecated') {
    return 'amber'
  }

  return 'gray'
}

function formatTimelineDate(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}

export function EventDefinitionDetailPage() {
  const { eventType } = useParams()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadDefinitions = hasPermission(currentUser, 'event-definitions.read')
  const canWriteDefinitions = hasPermission(currentUser, 'event-definitions.write')
  const canReadContracts = hasPermission(currentUser, 'contracts.read')

  const definitionQuery = useEventDefinitionDetailQuery(eventType)
  const schemasQuery = useEventSchemaVersionsQuery(eventType)
  const relatedContractsQuery = useRelatedContractsQuery(canReadContracts ? eventType : undefined)

  const updateMutation = useUpdateEventDefinitionMutation(eventType ?? '')
  const createSchemaMutation = useCreateSchemaDraftMutation(eventType ?? '')
  const activateSchemaMutation = useActivateSchemaVersionMutation(eventType ?? '')
  const deprecateSchemaMutation = useDeprecateSchemaVersionMutation(eventType ?? '')

  const [descriptionInput, setDescriptionInput] = useState('')
  const [displayNameInput, setDisplayNameInput] = useState('')
  const [schemaVersionInput, setSchemaVersionInput] = useState('')
  const [schemaJsonInput, setSchemaJsonInput] = useState('')
  const [compatibilityMode, setCompatibilityMode] = useState<'Backward' | 'Forward'>('Backward')
  const [selectedSchemaVersion, setSelectedSchemaVersion] = useState<number | undefined>(undefined)
  const [schemaDraftError, setSchemaDraftError] = useState<string | null>(null)
  const [copiedDefinitionField, setCopiedDefinitionField] = useState<string | null>(null)
  const [editingDefinitionField, setEditingDefinitionField] = useState<'displayName' | 'description' | null>(null)

  const selectedSchemaQuery = useEventSchemaVersionQuery(eventType, selectedSchemaVersion)

  const schemaVersionValue = Number.parseInt(schemaVersionInput, 10)
  const schemaVersionError =
    !schemaVersionInput.trim()
      ? 'Version is required.'
      : Number.isNaN(schemaVersionValue) || schemaVersionValue <= 0
        ? 'Version must be a positive number.'
        : null
  const schemaJsonError = !schemaJsonInput.trim() ? 'Schema JSON is required.' : null

  const definition = definitionQuery.data
  const schemaItems = schemasQuery.data?.items ?? []
  const relatedContracts = relatedContractsQuery.data ?? []

  const activeSchemaVersion = schemaItems.find((item) => item.state === 'Active')?.version
  const timelineSchemas = [...schemaItems].sort(
    (left, right) => new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
  )

  if (!canReadDefinitions) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Event definition detail</Heading>
          <Text color="gray">You do not have permission to read event definitions.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  if (!eventType) {
    return (
      <Card>
        <Text color="red">Invalid event type.</Text>
      </Card>
    )
  }

  if (definitionQuery.isLoading) {
    return (
      <Card>
        <Text size="2" color="gray">Loading definition...</Text>
      </Card>
    )
  }

  if (definitionQuery.isError || !definition) {
    const forbidden = definitionQuery.error instanceof ApiError && definitionQuery.error.status === 403
    const notFound = definitionQuery.error instanceof ApiError && definitionQuery.error.status === 404

    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Event definition detail</Heading>
          <Text color={forbidden || notFound ? 'gray' : 'red'}>
            {forbidden
              ? 'The backend denied access (403).'
              : notFound
                ? 'Event definition not found.'
                : getApiErrorMessage(definitionQuery.error, 'Unable to load event definition.')}
          </Text>
          <Button asChild variant="soft">
            <NavLink to="/event-definitions">Back to definitions</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  async function handleUpdateDefinition(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!definition) {
      return
    }

    const nextDisplayName = editingDefinitionField === 'displayName'
      ? displayNameInput.trim()
      : definition.displayName
    const nextDescription = editingDefinitionField === 'description'
      ? descriptionInput.trim() || null
      : definition.description

    if (!canWriteDefinitions || !nextDisplayName || nextDisplayName.length > 200 || (nextDescription?.length ?? 0) > 2000) {
      return
    }

    try {
      await updateMutation.mutateAsync({
        displayName: nextDisplayName,
        description: nextDescription,
      })
      setEditingDefinitionField(null)
      notifications.success('Event definition saved.', 'Event definition')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to update event definition.'), 'Event definition')
    }
  }

  async function handleActivateSchema(version: number) {
    try {
      await activateSchemaMutation.mutateAsync(version)
      notifications.success(`Schema v${version} activated.`, 'Event definition')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to activate schema version.'), 'Event definition')
    }
  }

  async function handleDeprecateSchema(version: number) {
    try {
      await deprecateSchemaMutation.mutateAsync(version)
      notifications.success(`Schema v${version} deprecated.`, 'Event definition')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to deprecate schema version.'), 'Event definition')
    }
  }

  async function handleCreateSchemaDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteDefinitions || schemaVersionError || schemaJsonError) {
      return
    }

    setSchemaDraftError(null)

    try {
      await createSchemaMutation.mutateAsync({
        version: schemaVersionValue,
        schema: schemaJsonInput,
        compatibilityMode,
      })

      setSchemaVersionInput('')
      setSchemaJsonInput('')
      notifications.success('Schema draft created.', 'Event definition')
    } catch (error) {
      setSchemaDraftError(getApiErrorMessage(error, 'Unable to create schema draft.'))
      notifications.error(getApiErrorMessage(error, 'Unable to create schema draft.'), 'Event definition')
    }
  }

  async function handleCopyDefinition(value: string, field: string) {
    try {
      let copied = false

      if (navigator.clipboard?.writeText) {
        try {
          await navigator.clipboard.writeText(value)
          copied = true
        } catch {
          copied = false
        }
      }

      if (!copied) {
        const textArea = document.createElement('textarea')
        textArea.value = value
        textArea.style.position = 'fixed'
        textArea.style.opacity = '0'
        document.body.appendChild(textArea)

        try {
          textArea.select()
          copied = document.execCommand('copy')
        } finally {
          document.body.removeChild(textArea)
        }

        if (!copied) {
          throw new Error('Copy command was rejected.')
        }
      }

      setCopiedDefinitionField(field)

      window.setTimeout(() => {
        setCopiedDefinitionField((current) => (current === field ? null : current))
      }, 1500)
    } catch {
      notifications.error('Unable to copy to the clipboard.', 'Copy')
    }
  }

  const definitionInformation = JSON.stringify({
    eventType: definition.eventType,
    displayName: definition.displayName,
    description: definition.description,
    createdAt: definition.createdAt,
  }, null, 2)

  return (
    <Flex direction="column" gap="4" className="detail-page">
      <DetailPageHeader
        title={definition.displayName || definition.eventType}
        description="Definition metadata, schema lifecycle, and subscribed contracts."
        icon={EventDefinitionsIcon}
        actions={(
          <Button asChild variant="soft">
            <NavLink to="/event-definitions">Back to list</NavLink>
          </Button>
        )}
      />

      <div className="visual-detail-metric-grid">
        <DetailMetric icon={EventsIcon} label="Event type" value={definition.eventType} supporting="Event routing key" />
        <DetailMetric icon={CodeIcon} label="Schema versions" value={schemaItems.length} supporting={activeSchemaVersion ? `Version ${activeSchemaVersion} active` : 'No active schema'} tone={activeSchemaVersion ? 'success' : 'warning'} />
        <DetailMetric icon={ContractsIcon} label="Related contracts" value={relatedContracts.length} supporting="Subscribed contracts" tone="violet" />
        <DetailMetric icon={CalendarIcon} label="Created" value={new Date(definition.createdAt).toLocaleString()} supporting="Definition registration" />
      </div>

      <Card className="detail-panel detail-panel-wide event-definition-timeline-card">
        <div className="event-section-header">
          <Flex align="center" gap="2">
            <span className="event-section-icon"><ClockIcon /></span>
            <div><Heading size="4">Schema timeline</Heading><Text size="1" color="gray">Definition registration and schema lifecycle in chronological order.</Text></div>
          </Flex>
        </div>
        <div className="event-timeline-scroll">
          <ol className="event-timeline">
            <li className="event-timeline-item event-timeline-received">
              <span className="event-timeline-marker"><EventDefinitionsIcon /></span>
              <div className="event-timeline-content">
                <Text size="1" color="gray">Definition created</Text>
                <Text size="2" weight="bold">{definition.displayName || definition.eventType}</Text>
                <Text size="1" color="gray">{formatTimelineDate(definition.createdAt)}</Text>
              </div>
            </li>
            {timelineSchemas.map((schema) => {
              const stateColor = mapSchemaStateColor(schema.state)

              return (
                <li className={`event-timeline-item event-timeline-${stateColor}`} key={schema.version}>
                  <span className="event-timeline-marker"><CodeIcon /></span>
                  <div className="event-timeline-content">
                    <Flex align="center" gap="1" wrap="wrap">
                      <Text size="1" color="gray">Schema version {schema.version}</Text>
                      <Badge color={stateColor} variant="soft">{schema.state}</Badge>
                    </Flex>
                    <Text size="2" weight="bold">{schema.compatibilityMode} compatibility</Text>
                    <Text size="1" color="gray">{formatTimelineDate(schema.createdAt)}</Text>
                  </div>
                </li>
              )
            })}
          </ol>
        </div>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-information-card definition-information-card">
        <div className="event-section-header">
          <Flex align="center" gap="2">
            <span className="event-section-icon"><EventDefinitionsIcon /></span>
            <div><Heading size="4">Definition detail</Heading><Text size="1" color="gray">Detailed information about this event definition.</Text></div>
          </Flex>
          <Button variant="soft" size="1" onClick={() => void handleCopyDefinition(definitionInformation, 'all')}>
            <CopyIcon />{copiedDefinitionField === 'all' ? 'Copied' : 'Copy all'}
          </Button>
        </div>
        <div className="event-information-list">
          <div className="event-information-row"><EventsIcon /><Text weight="medium">Event type</Text><Text>{definition.eventType}</Text><Button variant="ghost" size="1" aria-label={copiedDefinitionField === 'eventType' ? 'Event type copied' : 'Copy event type'} title={copiedDefinitionField === 'eventType' ? 'Copied' : 'Copy event type'} onClick={() => void handleCopyDefinition(definition.eventType, 'eventType')}>{copiedDefinitionField === 'eventType' ? <CheckCircleIcon /> : <CopyIcon />}</Button></div>
          <div className={`event-information-row definition-editable-row${editingDefinitionField === 'displayName' ? ' is-editing' : ''}`}>
            <EventDefinitionsIcon />
            <Text weight="medium">Display name</Text>
            {editingDefinitionField === 'displayName' ? (
              <form className="definition-inline-form" onSubmit={handleUpdateDefinition}>
                <TextField.Root value={displayNameInput} onChange={(entry) => setDisplayNameInput(entry.target.value)} placeholder="Display name" disabled={updateMutation.isPending} autoFocus aria-invalid={!displayNameInput.trim() || displayNameInput.trim().length > 200 ? 'true' : 'false'} />
                <FieldErrorText id="event-definition-display-name-error" message={!displayNameInput.trim() ? 'Display name is required.' : displayNameInput.trim().length > 200 ? 'Display name must be at most 200 characters.' : null} />
                <FormErrorText message={updateMutation.isError ? getApiErrorMessage(updateMutation.error, 'Unable to update event definition.') : null} />
                <Flex gap="2" justify="end" wrap="wrap"><Button type="button" variant="soft" disabled={updateMutation.isPending} onClick={() => setEditingDefinitionField(null)}>Cancel</Button><Button type="submit" disabled={updateMutation.isPending || !displayNameInput.trim() || displayNameInput.trim().length > 200}>{updateMutation.isPending ? 'Saving...' : 'Save display name'}</Button></Flex>
              </form>
            ) : (
              <><Text>{definition.displayName || '-'}</Text><Flex className="definition-row-actions" gap="1"><Button variant="ghost" size="1" aria-label={copiedDefinitionField === 'displayName' ? 'Display name copied' : 'Copy display name'} title={copiedDefinitionField === 'displayName' ? 'Copied' : 'Copy display name'} onClick={() => void handleCopyDefinition(definition.displayName, 'displayName')}>{copiedDefinitionField === 'displayName' ? <CheckCircleIcon /> : <CopyIcon />}</Button><Button variant="ghost" size="1" aria-label="Edit display name" title="Edit display name" disabled={!canWriteDefinitions} onClick={() => { setDisplayNameInput(definition.displayName); setEditingDefinitionField('displayName') }}><PencilIcon /></Button></Flex></>
            )}
          </div>
          <div className={`event-information-row definition-editable-row${editingDefinitionField === 'description' ? ' is-editing' : ''}`}>
            <CodeIcon />
            <Text weight="medium">Description</Text>
            {editingDefinitionField === 'description' ? (
              <form className="definition-inline-form" onSubmit={handleUpdateDefinition}>
                <TextArea
                  value={descriptionInput}
                  onChange={(entry) => setDescriptionInput(entry.target.value)}
                  placeholder="Add description"
                  rows={3}
                  disabled={updateMutation.isPending}
                  autoFocus
                />
                <FieldErrorText id="event-definition-description-validation-error" message={descriptionInput.trim().length > 2000 ? 'Description must be at most 2000 characters.' : null} />
                <FormErrorText
                  id="event-definition-description-error"
                  message={updateMutation.isError ? getApiErrorMessage(updateMutation.error, 'Unable to update event definition.') : null}
                />
                <Flex gap="2" justify="end" wrap="wrap">
                  <Button type="button" variant="soft" disabled={updateMutation.isPending} onClick={() => setEditingDefinitionField(null)}>Cancel</Button>
                  <Button type="submit" disabled={updateMutation.isPending || descriptionInput.trim().length > 2000}>{updateMutation.isPending ? 'Saving...' : 'Save description'}</Button>
                </Flex>
              </form>
            ) : (
              <>
                <Text>{definition.description || '-'}</Text>
                <Flex className="definition-row-actions" gap="1"><Button variant="ghost" size="1" aria-label={copiedDefinitionField === 'description' ? 'Description copied' : 'Copy description'} title={copiedDefinitionField === 'description' ? 'Copied' : 'Copy description'} onClick={() => void handleCopyDefinition(definition.description ?? '', 'description')}>{copiedDefinitionField === 'description' ? <CheckCircleIcon /> : <CopyIcon />}</Button><Button variant="ghost" size="1" aria-label="Edit description" title="Edit description" disabled={!canWriteDefinitions} onClick={() => { setDescriptionInput(definition.description ?? ''); setEditingDefinitionField('description') }}><PencilIcon /></Button></Flex>
              </>
            )}
          </div>
          <div className="event-information-row"><CalendarIcon /><Text weight="medium">Created</Text><Text>{formatTimelineDate(definition.createdAt)}</Text><Button variant="ghost" size="1" aria-label={copiedDefinitionField === 'createdAt' ? 'Creation timestamp copied' : 'Copy creation timestamp'} title={copiedDefinitionField === 'createdAt' ? 'Copied' : 'Copy creation timestamp'} onClick={() => void handleCopyDefinition(definition.createdAt, 'createdAt')}>{copiedDefinitionField === 'createdAt' ? <CheckCircleIcon /> : <CopyIcon />}</Button></div>
        </div>
      </Card>

      <Card className="detail-panel detail-panel-wide">
        <form onSubmit={handleCreateSchemaDraft}>
          <Flex direction="column" gap="3">
            <DetailSectionTitle icon={CodeIcon}>Schema versions</DetailSectionTitle>

            {schemasQuery.isLoading ? (
              <Text size="2" color="gray">Loading schema versions...</Text>
            ) : schemasQuery.isError ? (
              <Text size="2" color="red">
                {getApiErrorMessage(schemasQuery.error, 'Unable to load schema versions.')}
              </Text>
            ) : schemaItems.length === 0 ? (
              <Text size="2" color="gray">No schema versions yet.</Text>
            ) : (
              <Table.Root>
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>Version</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>State</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Compatibility</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Created</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {schemaItems.map((schema) => (
                    <Table.Row key={schema.version}>
                      <Table.RowHeaderCell>{schema.version}</Table.RowHeaderCell>
                      <Table.Cell>
                        <Badge color={mapSchemaStateColor(schema.state)} variant="soft">
                          {schema.state}
                        </Badge>
                      </Table.Cell>
                      <Table.Cell>{schema.compatibilityMode}</Table.Cell>
                      <Table.Cell>{new Date(schema.createdAt).toLocaleString()}</Table.Cell>
                      <Table.Cell>
                        <Flex gap="2" wrap="wrap">
                          <Button
                            size="1"
                            variant="ghost"
                            onClick={() => setSelectedSchemaVersion(schema.version)}
                          >
                            View schema
                          </Button>
                          <Button
                            size="1"
                            variant="soft"
                            disabled={!canWriteDefinitions || schema.state === 'Active'}
                            onClick={() => {
                              void handleActivateSchema(schema.version)
                            }}
                          >
                            Activate
                          </Button>
                          <Button
                            size="1"
                            variant="soft"
                            color="amber"
                            disabled={!canWriteDefinitions || schema.state === 'Deprecated'}
                            onClick={() => {
                              void handleDeprecateSchema(schema.version)
                            }}
                          >
                            Deprecate
                          </Button>
                        </Flex>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            )}

            <Text size="2" color="gray">
              Active version: {activeSchemaVersion ?? 'none'}
            </Text>

            {selectedSchemaVersion ? (
              <Card>
                <Flex direction="column" gap="2">
                  <Heading size="3">Schema viewer (v{selectedSchemaVersion})</Heading>
                  {selectedSchemaQuery.isLoading ? (
                    <Text size="2" color="gray">Loading schema...</Text>
                  ) : selectedSchemaQuery.isError ? (
                    <Text size="2" color="red">
                      {getApiErrorMessage(selectedSchemaQuery.error, 'Unable to load schema.')}
                    </Text>
                  ) : (
                    <ScrollArea type="auto" scrollbars="both" style={{ maxHeight: 320 }}>
                      <pre style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                        {selectedSchemaQuery.data?.schema ?? '{}'}
                      </pre>
                    </ScrollArea>
                  )}
                </Flex>
              </Card>
            ) : null}

            <Heading size="3">Create schema draft</Heading>
            <TextField.Root
              placeholder="Version (number)"
              value={schemaVersionInput}
              onChange={(entry) => setSchemaVersionInput(entry.target.value)}
              disabled={!canWriteDefinitions || createSchemaMutation.isPending}
              aria-invalid={schemaVersionError ? 'true' : 'false'}
              aria-describedby={schemaVersionError ? 'event-definition-schema-version-error' : undefined}
            />
            <FieldErrorText id="event-definition-schema-version-error" message={schemaVersionError} />
            <Select.Root
              value={compatibilityMode}
              onValueChange={(value) => setCompatibilityMode(value as 'Backward' | 'Forward')}
            >
              <Select.Trigger />
              <Select.Content>
                <Select.Item value="Backward">Backward</Select.Item>
                <Select.Item value="Forward">Forward</Select.Item>
              </Select.Content>
            </Select.Root>
            <TextArea
              placeholder="JSON schema"
              value={schemaJsonInput}
              onChange={(entry) => setSchemaJsonInput(entry.target.value)}
              rows={10}
              disabled={!canWriteDefinitions || createSchemaMutation.isPending}
              aria-invalid={schemaJsonError ? 'true' : 'false'}
              aria-describedby={schemaJsonError ? 'event-definition-schema-json-error' : undefined}
            />
            <FieldErrorText id="event-definition-schema-json-error" message={schemaJsonError} />

            <FormErrorText
              id="event-definition-schema-submit-error"
              message={
                schemaDraftError ??
                (createSchemaMutation.isError
                  ? getApiErrorMessage(createSchemaMutation.error, 'Unable to create schema draft.')
                  : null)
              }
            />

            <Button
              type="submit"
              disabled={!canWriteDefinitions || createSchemaMutation.isPending || !!schemaVersionError || !!schemaJsonError}
            >
              {createSchemaMutation.isPending ? 'Creating...' : 'Create schema draft'}
            </Button>
          </Flex>
        </form>
      </Card>

      <Card className="detail-panel detail-panel-wide">
        <Flex direction="column" gap="3">
          <DetailSectionTitle icon={ContractsIcon}>Related contracts</DetailSectionTitle>
          {!canReadContracts ? (
            <Text size="2" color="gray">Contracts are not available for your role.</Text>
          ) : relatedContractsQuery.isLoading ? (
            <Text size="2" color="gray">Loading related contracts...</Text>
          ) : relatedContractsQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(relatedContractsQuery.error, 'Unable to load related contracts.')}
            </Text>
          ) : relatedContracts.length === 0 ? (
            <Text size="2" color="gray">No contracts currently subscribe to this event type.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Enabled</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Destination</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {relatedContracts.map((contract) => (
                  <Table.Row key={contract.id}>
                    <Table.RowHeaderCell>
                      <NavLink to={`/contracts/${encodeURIComponent(contract.id)}`}>
                        {contract.id}
                      </NavLink>
                    </Table.RowHeaderCell>
                    <Table.Cell>{contract.customerId}</Table.Cell>
                    <Table.Cell>{contract.name ?? '-'}</Table.Cell>
                    <Table.Cell>
                      <Badge color={contract.enabled ? 'green' : 'gray'} variant="soft">
                        {contract.enabled ? 'Enabled' : 'Disabled'}
                      </Badge>
                    </Table.Cell>
                    <Table.Cell>{contract.destinationType}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
