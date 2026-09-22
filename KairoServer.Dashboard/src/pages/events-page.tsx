import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Select,
  Table,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { NavLink, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { useEventsQuery } from '../features/events/use-events'
import { useLocalUserPreferences } from '../features/settings/local-user-preferences'
import {
  deleteSavedView,
  getSavedViews,
  upsertSavedView,
  type SavedView,
} from '../features/saved-views/saved-views-storage'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DeliveryListItem, EventListQuery } from '../services/api/events-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'type' | 'source' | 'occurredAt' | 'receivedAt'
type SortDirection = 'asc' | 'desc'

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

function formatDeliveryStatus(status: number | string) {
  if (typeof status === 'string') {
    return status
  }

  const map: Record<number, string> = {
    0: 'Pending',
    1: 'InFlight',
    2: 'Delivered',
    3: 'Failed',
    4: 'Dead',
  }

  return map[status] ?? String(status)
}

function summarizeDeliveries(deliveries: DeliveryListItem[]) {
  if (deliveries.length === 0) {
    return {
      total: 0,
      pending: 0,
      failedOrDead: 0,
      delivered: 0,
    }
  }

  let pending = 0
  let failedOrDead = 0
  let delivered = 0

  deliveries.forEach((item) => {
    const normalized = formatDeliveryStatus(item.status).toLowerCase()
    if (normalized === 'pending' || normalized === 'inflight') {
      pending += 1
      return
    }

    if (normalized === 'failed' || normalized === 'dead') {
      failedOrDead += 1
      return
    }

    if (normalized === 'delivered') {
      delivered += 1
    }
  })

  return {
    total: deliveries.length,
    pending,
    failedOrDead,
    delivered,
  }
}

export function EventsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector((state) => state.auth)
  const { preferences } = useLocalUserPreferences(currentUser?.subject)
  const [savedViews, setSavedViews] = useState<SavedView[]>([])
  const [selectedSavedViewId, setSelectedSavedViewId] = useState('')
  const [savedViewName, setSavedViewName] = useState('')

  const canReadEvents = hasPermission(currentUser, 'events.read')
  const defaultPageSize = preferences.tablePageSizes.events || preferences.defaultPageSize

  const idParam = searchParams.get('id') ?? ''
  const typeParam = searchParams.get('type') ?? ''
  const sourceParam = searchParams.get('source') ?? ''
  const occurredFromParam = searchParams.get('occurredFrom') ?? ''
  const occurredToParam = searchParams.get('occurredTo') ?? ''
  const receivedFromParam = searchParams.get('receivedFrom') ?? ''
  const receivedToParam = searchParams.get('receivedTo') ?? ''
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('receivedAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ??
    ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), defaultPageSize)

  const [idInput, setIdInput] = useState(idParam)
  const [typeInput, setTypeInput] = useState(typeParam)
  const [sourceInput, setSourceInput] = useState(sourceParam)

  useEffect(() => {
    setSavedViews(getSavedViews(currentUser?.subject, 'events'))
  }, [currentUser?.subject])

  const query = useMemo<EventListQuery>(
    () => ({
      type: typeParam,
      source: sourceParam,
      occurredFrom: occurredFromParam || undefined,
      occurredTo: occurredToParam || undefined,
      receivedFrom: receivedFromParam || undefined,
      receivedTo: receivedToParam || undefined,
      sortBy: sortByParam,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [
      occurredFromParam,
      occurredToParam,
      pageParam,
      pageSizeParam,
      receivedFromParam,
      receivedToParam,
      sortByParam,
      sortDirectionParam,
      sourceParam,
      typeParam,
    ],
  )

  const eventsQuery = useEventsQuery(query)

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

  function handleSaveCurrentView() {
    if (!currentUser?.subject || !savedViewName.trim()) {
      return
    }

    const next = upsertSavedView(currentUser.subject, 'events', {
      name: savedViewName.trim(),
      query: searchParams.toString(),
    })

    setSavedViews(next)
    setSavedViewName('')
  }

  function handleApplySavedView() {
    if (!selectedSavedViewId) {
      return
    }

    const selected = savedViews.find((item) => item.id === selectedSavedViewId)
    if (!selected) {
      return
    }

    setSearchParams(new URLSearchParams(selected.query))
  }

  function handleDeleteSavedView() {
    if (!currentUser?.subject || !selectedSavedViewId) {
      return
    }

    const selected = savedViews.find((item) => item.id === selectedSavedViewId)
    if (!selected) {
      return
    }

    if (!window.confirm(`Delete saved view "${selected.name}"?`)) {
      return
    }

    const next = deleteSavedView(currentUser.subject, 'events', selectedSavedViewId)
    setSavedViews(next)
    setSelectedSavedViewId('')
  }

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    updateParams({
      id: idInput.trim() || null,
      type: typeInput.trim() || null,
      source: sourceInput.trim() || null,
      page: '1',
    })
  }

  function handleSort(sortBy: SortBy) {
    const isSame = sortByParam === sortBy
    const nextDirection: SortDirection =
      isSame && sortDirectionParam === 'asc' ? 'desc' : 'asc'

    updateParams({
      sortBy,
      sortDirection: nextDirection,
      page: '1',
    })
  }

  if (!canReadEvents) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Events</Heading>
          <Text color="gray">You do not have permission to read events.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const events = eventsQuery.data?.items ?? []
  const filteredById = idParam.trim()
    ? events.filter((item) =>
        item.id.toLowerCase().includes(idParam.trim().toLowerCase()),
      )
    : events

  const paging = eventsQuery.data

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Events</Heading>

      <Card>
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
              <TextField.Root
                placeholder="Search by event ID"
                value={idInput}
                onChange={(event) => setIdInput(event.target.value)}
              />

              <TextField.Root
                placeholder="Type"
                value={typeInput}
                onChange={(event) => setTypeInput(event.target.value)}
              />

              <TextField.Root
                placeholder="Source"
                value={sourceInput}
                onChange={(event) => setSourceInput(event.target.value)}
              />
            </Flex>

            <Flex wrap="wrap" gap="2" align="center">
              <Text size="2" color="gray">Occurred from</Text>
              <input
                type="datetime-local"
                value={occurredFromParam}
                onChange={(event) =>
                  updateParams({
                    occurredFrom: event.target.value || null,
                    page: '1',
                  })
                }
              />
              <Text size="2" color="gray">Occurred to</Text>
              <input
                type="datetime-local"
                value={occurredToParam}
                onChange={(event) =>
                  updateParams({
                    occurredTo: event.target.value || null,
                    page: '1',
                  })
                }
              />
            </Flex>

            <Flex wrap="wrap" gap="2" align="center">
              <Text size="2" color="gray">Received from</Text>
              <input
                type="datetime-local"
                value={receivedFromParam}
                onChange={(event) =>
                  updateParams({
                    receivedFrom: event.target.value || null,
                    page: '1',
                  })
                }
              />
              <Text size="2" color="gray">Received to</Text>
              <input
                type="datetime-local"
                value={receivedToParam}
                onChange={(event) =>
                  updateParams({
                    receivedTo: event.target.value || null,
                    page: '1',
                  })
                }
              />
            </Flex>

            <Flex gap="2">
              <Button type="submit">Apply filters</Button>
              <Select.Root
                value={String(pageSizeParam)}
                onValueChange={(value) => updateParams({ pageSize: value, page: '1' })}
              >
                <Select.Trigger />
                <Select.Content>
                  <Select.Item value="10">10 rows</Select.Item>
                  <Select.Item value="20">20 rows</Select.Item>
                  <Select.Item value="50">50 rows</Select.Item>
                  <Select.Item value="100">100 rows</Select.Item>
                </Select.Content>
              </Select.Root>
              <Button
                type="button"
                variant="soft"
                onClick={() => {
                  setIdInput('')
                  setTypeInput('')
                  setSourceInput('')
                  setSearchParams(
                    new URLSearchParams({
                      page: '1',
                      pageSize: String(defaultPageSize),
                    }),
                  )
                }}
              >
                Reset
              </Button>
            </Flex>

            <Flex direction="column" gap="2">
              <Text size="2" color="gray">Saved views</Text>
              <Flex wrap="wrap" gap="2" align="center">
                <Select.Root value={selectedSavedViewId} onValueChange={setSelectedSavedViewId}>
                  <Select.Trigger placeholder="Choose saved view" />
                  <Select.Content>
                    {savedViews.map((view) => (
                      <Select.Item key={view.id} value={view.id}>
                        {view.name}
                      </Select.Item>
                    ))}
                  </Select.Content>
                </Select.Root>
                <Button type="button" variant="soft" onClick={handleApplySavedView}>
                  Apply saved view
                </Button>
                <Button type="button" variant="outline" color="red" onClick={handleDeleteSavedView}>
                  Delete
                </Button>
              </Flex>
              <Flex wrap="wrap" gap="2" align="center">
                <TextField.Root
                  placeholder="Save current filters as..."
                  value={savedViewName}
                  onChange={(event) => setSavedViewName(event.target.value)}
                />
                <Button type="button" onClick={handleSaveCurrentView}>
                  Save current view
                </Button>
              </Flex>
            </Flex>
          </Flex>
        </form>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Event table</Heading>

          {eventsQuery.isLoading ? (
            <Text size="2" color="gray">Loading events...</Text>
          ) : eventsQuery.isError ? (
            <Text size="2" color="red">
              {eventsQuery.error instanceof ApiError && eventsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(eventsQuery.error, 'Unable to load events.')}
            </Text>
          ) : filteredById.length === 0 ? (
            <Text size="2" color="gray">No events found for current filters.</Text>
          ) : (
            <>
              <Table.Root size={preferences.denseTables ? '1' : '2'}>
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('type')}>
                        Type
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('source')}>
                        Source
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('occurredAt')}>
                        Occurred
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('receivedAt')}>
                        Received
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Delivery summary</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {filteredById.map((item) => {
                    const summary = summarizeDeliveries(item.deliveries)

                    return (
                      <Table.Row key={item.id}>
                        <Table.RowHeaderCell>{item.id}</Table.RowHeaderCell>
                        <Table.Cell>{item.type}</Table.Cell>
                        <Table.Cell>{item.source}</Table.Cell>
                        <Table.Cell>{new Date(item.occurredAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>{new Date(item.receivedAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>
                          <Flex gap="1" wrap="wrap">
                            <Badge variant="soft">Total {summary.total}</Badge>
                            <Badge color="green" variant="soft">Delivered {summary.delivered}</Badge>
                            <Badge color="amber" variant="soft">Pending {summary.pending}</Badge>
                            <Badge color="red" variant="soft">Failed/Dead {summary.failedOrDead}</Badge>
                          </Flex>
                        </Table.Cell>
                        <Table.Cell>
                          <Button asChild size="1" variant="ghost">
                            <NavLink to={`/events/${encodeURIComponent(item.id)}`}>
                              Detail
                            </NavLink>
                          </Button>
                        </Table.Cell>
                      </Table.Row>
                    )
                  })}
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
