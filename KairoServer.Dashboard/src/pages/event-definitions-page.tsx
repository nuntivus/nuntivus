import {
  Button,
  Card,
  Flex,
  Heading,
  Table,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink, useNavigate, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useCreateEventDefinitionMutation,
  useEventDefinitionsQuery,
} from '../features/event-definitions/use-event-definitions'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

function parsePositiveInt(value: string | null, fallback: number) {
  if (!value) {
    return fallback
  }

  const parsed = Number.parseInt(value, 10)
  if (Number.isNaN(parsed) || parsed < 1) {
    return fallback
  }

  return parsed
}

export function EventDefinitionsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
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

  function updateParams(updates: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams)

    Object.entries(updates).forEach(([key, value]) => {
      if (!value) {
        next.delete(key)
        return
      }

      next.set(key, value)
    })

    setSearchParams(next)
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteDefinitions || !eventType.trim() || !displayName.trim()) {
      return
    }

    const created = await createMutation.mutateAsync({
      eventType: eventType.trim(),
      displayName: displayName.trim(),
      description: description.trim() || null,
    })

    setEventType('')
    setDisplayName('')
    setDescription('')

    navigate(`/event-definitions/${encodeURIComponent(created.eventType)}`)
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
    <Flex direction="column" gap="4">
      <Heading size="6">Event Definitions</Heading>

      {canWriteDefinitions ? (
        <Card>
          <form onSubmit={handleCreate}>
            <Flex direction="column" gap="3">
              <Heading size="4">Create definition</Heading>

              <TextField.Root
                placeholder="Event type (orders.created)"
                value={eventType}
                onChange={(entry) => setEventType(entry.target.value)}
              />

              <TextField.Root
                placeholder="Display name"
                value={displayName}
                onChange={(entry) => setDisplayName(entry.target.value)}
              />

              <TextArea
                placeholder="Description (optional)"
                value={description}
                onChange={(entry) => setDescription(entry.target.value)}
                rows={3}
              />

              {createMutation.isError ? (
                <Text size="2" color="red">
                  {getApiErrorMessage(createMutation.error, 'Unable to create event definition.')}
                </Text>
              ) : null}

              <Button
                type="submit"
                disabled={createMutation.isPending || !eventType.trim() || !displayName.trim()}
              >
                {createMutation.isPending ? 'Creating...' : 'Create definition'}
              </Button>
            </Flex>
          </form>
        </Card>
      ) : null}

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Definition list</Heading>

          {definitionsQuery.isLoading ? (
            <Text size="2" color="gray">Loading definitions...</Text>
          ) : definitionsQuery.isError ? (
            <Text size="2" color="red">
              {definitionsQuery.error instanceof ApiError && definitionsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(definitionsQuery.error, 'Unable to load definitions.')}
            </Text>
          ) : definitions.length === 0 ? (
            <Text size="2" color="gray">No event definitions found.</Text>
          ) : (
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
                    <Table.Row key={definition.eventType}>
                      <Table.RowHeaderCell>{definition.eventType}</Table.RowHeaderCell>
                      <Table.Cell>{definition.displayName}</Table.Cell>
                      <Table.Cell>{definition.description ?? '-'}</Table.Cell>
                      <Table.Cell>{new Date(definition.createdAt).toLocaleString()}</Table.Cell>
                      <Table.Cell>
                        <Button asChild size="1" variant="ghost">
                          <NavLink to={`/event-definitions/${encodeURIComponent(definition.eventType)}`}>
                            Detail
                          </NavLink>
                        </Button>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>

              <Flex align="center" justify="between" gap="3">
                <Text size="2" color="gray">
                  {paging
                    ? `Page ${paging.page} of ${paging.totalPages} · ${paging.totalCount} total`
                    : ''}
                </Text>

                <Flex gap="2">
                  <Button
                    variant="soft"
                    disabled={!paging?.hasPrevious}
                    onClick={() => updateParams({ page: String(Math.max(1, pageParam - 1)) })}
                  >
                    Previous
                  </Button>
                  <Button
                    variant="soft"
                    disabled={!paging?.hasNext}
                    onClick={() => updateParams({ page: String(pageParam + 1) })}
                  >
                    Next
                  </Button>
                </Flex>
              </Flex>
            </>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
