import {
  Button,
  Card,
  Dialog,
  Flex,
  Heading,
  Table,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useRef, useState, type FormEvent } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { FormDialog } from '../components/dialogs/form-dialog'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { DetailIndicator, NavigableTableRow } from '../components/tables/table-actions'
import {
  useCreateEventDefinitionMutation,
  useEventDefinitionsQuery,
} from '../features/event-definitions/use-event-definitions'
import { useNotifications } from '../hooks/use-notifications'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

export function EventDefinitionsPage() {
  const { searchParams, updateParams } = useUrlQueryParams()
  const navigate = useNavigate()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadDefinitions = hasPermission(currentUser, 'event-definitions.read')
  const canWriteDefinitions = hasPermission(currentUser, 'event-definitions.write')

  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), 20)

  const query = useMemo(
    () => ({
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [pageParam, pageSizeParam],
  )

  const definitionsQuery = useEventDefinitionsQuery(query)
  const createMutation = useCreateEventDefinitionMutation()

  const [eventType, setEventType] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [description, setDescription] = useState('')
  const [isCreateOpen, setIsCreateOpen] = useState(false)
  const createButtonRef = useRef<HTMLButtonElement>(null)
  const eventTypeError = !eventType.trim() ? 'Event type is required.' : null
  const displayNameError = !displayName.trim() ? 'Display name is required.' : null

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteDefinitions || !!eventTypeError || !!displayNameError) {
      return
    }

    try {
      const created = await createMutation.mutateAsync({
        eventType: eventType.trim(),
        displayName: displayName.trim(),
        description: description.trim() || null,
      })

      setEventType('')
      setDisplayName('')
      setDescription('')
      setIsCreateOpen(false)
      notifications.success('Event definition created.', 'Create definition')

      navigate(`/event-definitions/${encodeURIComponent(created.eventType)}`)
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to create event definition.'), 'Create definition')
    }
  }

  function handleCreateOpenChange(open: boolean) {
    setIsCreateOpen(open)

    if (!open) {
      window.setTimeout(() => createButtonRef.current?.focus(), 0)
    }
  }

  if (!canReadDefinitions) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Event Definitions</Heading>
          <Text color="gray">You do not have permission to read event definitions.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const definitions = definitionsQuery.data?.items ?? []
  const paging = definitionsQuery.data

  return (
    <Flex direction="column" gap="5">
      <div className="page-header">
        <div>
          <h1 className="page-title">Event Definitions</h1>
          <p className="page-subtitle">
            Catalog the event types your producers can emit.
          </p>
        </div>
        {canWriteDefinitions ? (
          <Button
            ref={createButtonRef}
            size="2"
            onClick={() => setIsCreateOpen(true)}
          >
            New definition
          </Button>
        ) : null}
      </div>

      {canWriteDefinitions ? (
        <FormDialog
          open={isCreateOpen}
          onOpenChange={handleCreateOpenChange}
          title="Create definition"
          description="Register an event type with a friendly display name."
          descriptionId="event-definitions-create-dialog-description"
          footer={
            <>
              <Dialog.Close>
                <Button type="button" variant="soft" color="gray">
                  Cancel
                </Button>
              </Dialog.Close>
              <Button
                type="submit"
                form="event-definitions-create-form"
                disabled={createMutation.isPending || !!eventTypeError || !!displayNameError}
              >
                {createMutation.isPending ? 'Creating…' : 'Create definition'}
              </Button>
            </>
          }
        >
          <form id="event-definitions-create-form" onSubmit={handleCreate}>
            <div className="field">
              <label className="field-label" htmlFor="event-definitions-event-type">
                Event type
              </label>
              <TextField.Root
                id="event-definitions-event-type"
                size="2"
                placeholder="orders.created"
                value={eventType}
                onChange={(entry) => setEventType(entry.target.value)}
                aria-invalid={eventTypeError ? 'true' : 'false'}
                aria-describedby={eventTypeError ? 'event-definitions-event-type-error' : undefined}
              />
              <FieldErrorText id="event-definitions-event-type-error" message={eventTypeError} />
            </div>

            <div className="field">
              <label className="field-label" htmlFor="event-definitions-display-name">
                Display name
              </label>
              <TextField.Root
                id="event-definitions-display-name"
                size="2"
                placeholder="Order created"
                value={displayName}
                onChange={(entry) => setDisplayName(entry.target.value)}
                aria-invalid={displayNameError ? 'true' : 'false'}
                aria-describedby={displayNameError ? 'event-definitions-display-name-error' : undefined}
              />
              <FieldErrorText id="event-definitions-display-name-error" message={displayNameError} />
            </div>

            <div className="field">
              <label className="field-label" htmlFor="event-definitions-description">
                Description <span className="field-optional">(optional)</span>
              </label>
              <TextArea
                id="event-definitions-description"
                placeholder="Describe when this event is emitted."
                value={description}
                onChange={(entry) => setDescription(entry.target.value)}
                rows={3}
              />
            </div>

            <FormErrorText
              id="event-definitions-create-error"
              message={
                createMutation.isError
                  ? getApiErrorMessage(createMutation.error, 'Unable to create event definition.')
                  : null
              }
            />
          </form>
        </FormDialog>
      ) : null}

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Definition list</Heading>

          <ListFeedback
            isLoading={definitionsQuery.isLoading}
            isError={definitionsQuery.isError}
            error={definitionsQuery.error}
            isEmpty={definitions.length === 0}
            loadingMessage="Loading definitions..."
            errorMessage={
              definitionsQuery.error instanceof ApiError && definitionsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(definitionsQuery.error, 'Unable to load definitions.')
            }
            emptyMessage="No event definitions found."
          >
            <>
              <Table.Root>
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>Event type</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Display name</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Description</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Created</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {definitions.map((definition) => (
                    <NavigableTableRow
                      key={definition.eventType}
                      to={`/event-definitions/${encodeURIComponent(definition.eventType)}`}
                      label={`event definition ${definition.eventType}`}
                    >
                      <Table.RowHeaderCell>{definition.eventType}</Table.RowHeaderCell>
                      <Table.Cell>{definition.displayName}</Table.Cell>
                      <Table.Cell>{definition.description ?? '-'}</Table.Cell>
                      <Table.Cell>{new Date(definition.createdAt).toLocaleString()}</Table.Cell>
                      <Table.Cell>
                        <div className="table-row-actions">
                          <DetailIndicator />
                        </div>
                      </Table.Cell>
                    </NavigableTableRow>
                  ))}
                </Table.Body>
              </Table.Root>

              <ListPagination
                paging={paging}
                onPrevious={() => updateParams({ page: String(Math.max(1, pageParam - 1)) })}
                onNext={() => updateParams({ page: String(pageParam + 1) })}
              />
            </>
          </ListFeedback>
        </Flex>
      </Card>
    </Flex>
  )
}
