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
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { EntityReference } from '../components/links/entity-reference'
import { hasPermission } from '../auth/permissions'
import { ListFeedback } from '../components/lists/list-feedback'
import { SearchIcon } from '../components/icons/nav-icons'
import { ListPagination } from '../components/lists/list-pagination'
import { DetailAction, NavigableTableRow } from '../components/tables/table-actions'
import { useEventsQuery } from '../features/events/use-events'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DeliveryListItem, EventListQuery } from '../services/api/events-api'
import { useAppSelector } from '../store/hooks'
import { DataExportButtons } from '../components/exports/data-export-buttons'

type SortBy = 'type' | 'source' | 'occurredAt' | 'receivedAt'
type SortDirection = 'asc' | 'desc'

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
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadEvents = hasPermission(currentUser, 'events.read')
  const defaultPageSize = 20

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
  const hasActiveFilters =
    idParam.trim().length > 0 ||
    typeParam.trim().length > 0 ||
    sourceParam.trim().length > 0 ||
    occurredFromParam.trim().length > 0 ||
    occurredToParam.trim().length > 0 ||
    receivedFromParam.trim().length > 0 ||
    receivedToParam.trim().length > 0
  const filteredById = idParam.trim()
    ? events.filter((item) =>
      item.id.toLowerCase().includes(idParam.trim().toLowerCase()),
    )
    : events

  const paging = eventsQuery.data

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Events</h1>
          <p className="page-subtitle">Inspect accepted events, delivery outcomes, and source activity.</p>
        </div>
        <DataExportButtons entity="events" filters={{ ...query, id: idParam }} />
      </div>

      <Card className="filter-card">
        <form onSubmit={handleSearchSubmit}>
          <div className="filter-panel">
            <div className="filter-panel-header">
              <h2 className="filter-panel-title">Filters</h2>
            </div>

            <div className="filter-grid">
              <div className="filter-field">
                <label className="filter-label" htmlFor="events-filter-id">
                  Event ID
                </label>
                <TextField.Root
                  id="events-filter-id"
                  size="2"
                  aria-label="Event ID"
                  placeholder="Search by event ID"
                  value={idInput}
                  onChange={(event) => setIdInput(event.target.value)}
                >
                  <TextField.Slot>
                    <SearchIcon width={15} height={15} />
                  </TextField.Slot>
                </TextField.Root>
              </div>

              <div className="filter-field">
                <label className="filter-label" htmlFor="events-filter-type">
                  Type
                </label>
                <TextField.Root
                  id="events-filter-type"
                  size="2"
                  aria-label="Event type"
                  placeholder="orders.created"
                  value={typeInput}
                  onChange={(event) => setTypeInput(event.target.value)}
                />
              </div>

              <div className="filter-field">
                <label className="filter-label" htmlFor="events-filter-source">
                  Source
                </label>
                <TextField.Root
                  id="events-filter-source"
                  size="2"
                  aria-label="Source"
                  placeholder="service name"
                  value={sourceInput}
                  onChange={(event) => setSourceInput(event.target.value)}
                />
              </div>

              <div className="filter-field">
                <span className="filter-label">Occurred</span>
                <div className="filter-daterange">
                  <input
                    className="filter-datetime"
                    aria-label="Occurred from"
                    type="datetime-local"
                    value={occurredFromParam}
                    onChange={(event) =>
                      updateParams({
                        occurredFrom: event.target.value || null,
                        page: '1',
                      })
                    }
                  />
                  <input
                    className="filter-datetime"
                    aria-label="Occurred to"
                    type="datetime-local"
                    value={occurredToParam}
                    onChange={(event) =>
                      updateParams({
                        occurredTo: event.target.value || null,
                        page: '1',
                      })
                    }
                  />
                </div>
              </div>

              <div className="filter-field filter-field-wide">
                <span className="filter-label">Received</span>
                <div className="filter-daterange">
                  <input
                    className="filter-datetime"
                    aria-label="Received from"
                    type="datetime-local"
                    value={receivedFromParam}
                    onChange={(event) =>
                      updateParams({
                        receivedFrom: event.target.value || null,
                        page: '1',
                      })
                    }
                  />
                  <input
                    className="filter-datetime"
                    aria-label="Received to"
                    type="datetime-local"
                    value={receivedToParam}
                    onChange={(event) =>
                      updateParams({
                        receivedTo: event.target.value || null,
                        page: '1',
                      })
                    }
                  />
                </div>
              </div>
            </div>

            <div className="filter-actions">
              <Button type="submit" size="2">
                Apply filters
              </Button>
              <Button
                type="button"
                size="2"
                variant="soft"
                color="gray"
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
              <div className="filter-actions-spacer" />
              <Select.Root
                size="2"
                value={String(pageSizeParam)}
                onValueChange={(value) => updateParams({ pageSize: value, page: '1' })}
              >
                <Select.Trigger aria-label="Rows per page" />
                <Select.Content>
                  <Select.Item value="10">10 rows</Select.Item>
                  <Select.Item value="20">20 rows</Select.Item>
                  <Select.Item value="50">50 rows</Select.Item>
                  <Select.Item value="100">100 rows</Select.Item>
                </Select.Content>
              </Select.Root>
            </div>

          </div>
        </form>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Event table</Heading>

          <ListFeedback
            isLoading={eventsQuery.isLoading}
            isError={eventsQuery.isError}
            error={eventsQuery.error}
            isEmpty={filteredById.length === 0}
            loadingMessage="Loading events..."
            errorMessage={
              eventsQuery.error instanceof ApiError && eventsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(eventsQuery.error, 'Unable to load events.')
            }
            emptyMessage="No events found for current filters."
            emptyTitle={hasActiveFilters ? 'No events match current filters' : 'No events yet'}
            emptyDescription={
              hasActiveFilters
                ? 'Try widening the search criteria or clearing filters.'
                : 'Events will appear here when producers submit new event payloads.'
            }
            emptyAction={
              hasActiveFilters
                ? {
                  label: 'Clear filters',
                  onClick: () =>
                    setSearchParams(
                      new URLSearchParams({
                        page: '1',
                        pageSize: String(defaultPageSize),
                      }),
                    ),
                }
                : {
                  label: 'Refresh',
                  onClick: () => {
                    void eventsQuery.refetch()
                  },
                }
            }
          >
            <>
              <div className="responsive-table">
                <Table.Root size="2" aria-label="Events table">
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
                        <NavigableTableRow
                          key={item.id}
                          to={`/events/${encodeURIComponent(item.id)}`}
                          label={`event ${item.type}`}
                        >
                          <Table.RowHeaderCell>{item.id}</Table.RowHeaderCell>
                          <Table.Cell>{item.type}</Table.Cell>
                          <Table.Cell><EntityReference kind="source" id={item.source} /></Table.Cell>
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
                            <div className="table-row-actions">
                              <DetailAction
                                to={`/events/${encodeURIComponent(item.id)}`}
                                label={`Open event ${item.type}`}
                              />
                            </div>
                          </Table.Cell>
                        </NavigableTableRow>
                      )
                    })}
                  </Table.Body>
                </Table.Root>
              </div>

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
