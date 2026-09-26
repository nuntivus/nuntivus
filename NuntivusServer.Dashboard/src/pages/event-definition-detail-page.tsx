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
import {
  useActivateSchemaVersionMutation,
  useCreateSchemaDraftMutation,
  useDeprecateSchemaVersionMutation,
  useEventDefinitionDetailQuery,
  useEventSchemaVersionQuery,
  useEventSchemaVersionsQuery,
  useRelatedContractsQuery,
  useUpdateEventDefinitionDescriptionMutation,
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

  const descriptionMutation = useUpdateEventDefinitionDescriptionMutation(eventType ?? '')
  const createSchemaMutation = useCreateSchemaDraftMutation(eventType ?? '')
  const activateSchemaMutation = useActivateSchemaVersionMutation(eventType ?? '')
  const deprecateSchemaMutation = useDeprecateSchemaVersionMutation(eventType ?? '')

  const [descriptionInput, setDescriptionInput] = useState('')
  const [schemaVersionInput, setSchemaVersionInput] = useState('')
  const [schemaJsonInput, setSchemaJsonInput] = useState('')
  const [compatibilityMode, setCompatibilityMode] = useState<'Backward' | 'Forward'>('Backward')
  const [selectedSchemaVersion, setSelectedSchemaVersion] = useState<number | undefined>(undefined)
  const [schemaDraftError, setSchemaDraftError] = useState<string | null>(null)

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

  async function handleUpdateDescription(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteDefinitions) {
      return
    }

    try {
      await descriptionMutation.mutateAsync({
        description: descriptionInput.trim() || null,
      })
      notifications.success('Description saved.', 'Event definition')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to update description.'), 'Event definition')
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

  return (
    <Flex direction="column" gap="4">
      <Flex align="center" justify="between" wrap="wrap" gap="3">
        <Heading size="6">Event definition: {definition.eventType}</Heading>
        <Button asChild variant="soft">
          <NavLink to="/event-definitions">Back to list</NavLink>
        </Button>
      </Flex>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Definition detail</Heading>
          <Text size="2" color="gray">Event type</Text>
          <Text>{definition.eventType}</Text>
          <Text size="2" color="gray">Display name</Text>
          <Text>{definition.displayName}</Text>
          <Text size="2" color="gray">Created</Text>
          <Text>{new Date(definition.createdAt).toLocaleString()}</Text>
        </Flex>
      </Card>

      <Card>
        <form onSubmit={handleUpdateDescription}>
          <Flex direction="column" gap="3">
            <Heading size="4">Edit description</Heading>
            <TextArea
              value={descriptionInput}
              onChange={(entry) => setDescriptionInput(entry.target.value)}
              placeholder={definition.description ?? 'Add description'}
              rows={4}
              disabled={!canWriteDefinitions || descriptionMutation.isPending}
            />

            <FormErrorText
              id="event-definition-description-error"
              message={
                descriptionMutation.isError
                  ? getApiErrorMessage(
                      descriptionMutation.error,
                      'Unable to update description.',
                    )
                  : null
              }
            />

            <Button type="submit" disabled={!canWriteDefinitions || descriptionMutation.isPending}>
              {descriptionMutation.isPending ? 'Saving...' : 'Save description'}
            </Button>
          </Flex>
        </form>
      </Card>

      <Card>
        <form onSubmit={handleCreateSchemaDraft}>
          <Flex direction="column" gap="3">
            <Heading size="4">Schema versions</Heading>

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

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Related contracts</Heading>
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
