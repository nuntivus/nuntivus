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
import { useDeliveriesQuery } from '../features/deliveries/use-deliveries'
import { useLocalUserPreferences } from '../features/settings/local-user-preferences'
import {
  deleteSavedView,
  getSavedViews,
  upsertSavedView,
  type SavedView,
} from '../features/saved-views/saved-views-storage'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type {
  DeliveryListItem,
  DeliveryListQuery,
  DeliveryStatus,
  DestinationType,
} from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'createdAt' | 'updatedAt'
type SortDirection = 'asc' | 'desc'

const destinationValues: DestinationType[] = ['Http', 'Kafka', 'Nats', 'RabbitMq', 'Sqs']

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

function formatDeliveryStatus(status: DeliveryStatus) {
  return typeof status === 'string' ? status : String(status)
}

function isFailedOrRetrying(item: DeliveryListItem) {
  const normalized = formatDeliveryStatus(item.status).toLowerCase()
  return normalized === 'failed' || normalized === 'inflight'
}

export function DeliveriesPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector((state) => state.auth)
  const { preferences } = useLocalUserPreferences(currentUser?.subject)
  const [savedViews, setSavedViews] = useState<SavedView[]>([])
  const [selectedSavedViewId, setSelectedSavedViewId] = useState('')
  const [savedViewName, setSavedViewName] = useState('')

  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const defaultPageSize =
    preferences.tablePageSizes.deliveries || preferences.defaultPageSize

  const eventIdParam = searchParams.get('eventId') ?? ''
  const customerIdParam = searchParams.get('customerId') ?? ''
  const contractIdParam = searchParams.get('contractId') ?? ''
  const statusParam = (searchParams.get('status') as DeliveryStatus | null) ?? undefined
  const destinationTypeParam =
    (searchParams.get('destinationType') as DestinationType | null) ?? undefined
  const createdFromParam = searchParams.get('createdFrom') ?? ''
  const createdToParam = searchParams.get('createdTo') ?? ''
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('updatedAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ?? ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), defaultPageSize)

  const [eventIdInput, setEventIdInput] = useState(eventIdParam)
  const [customerIdInput, setCustomerIdInput] = useState(customerIdParam)
  const [contractIdInput, setContractIdInput] = useState(contractIdParam)

  useEffect(() => {
    setSavedViews(getSavedViews(currentUser?.subject, 'deliveries'))
  }, [currentUser?.subject])

  const query = useMemo<DeliveryListQuery>(
    () => ({
      eventId: eventIdParam,
      customerId: customerIdParam,
      contractId: contractIdParam,
      status: statusParam,
      destinationType: destinationTypeParam,
      createdFrom: createdFromParam || undefined,
      createdTo: createdToParam || undefined,
      sortBy: sortByParam,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [
      contractIdParam,
      createdFromParam,
      createdToParam,
      customerIdParam,
      destinationTypeParam,
      eventIdParam,
      pageParam,
      pageSizeParam,
      sortByParam,
      sortDirectionParam,
      statusParam,
    ],
  )

  const deliveriesQuery = useDeliveriesQuery(query)

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

    const next = upsertSavedView(currentUser.subject, 'deliveries', {
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

    const next = deleteSavedView(currentUser.subject, 'deliveries', selectedSavedViewId)
    setSavedViews(next)
    setSelectedSavedViewId('')
  }

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    updateParams({
      eventId: eventIdInput.trim() || null,
      customerId: customerIdInput.trim() || null,
      contractId: contractIdInput.trim() || null,
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

  if (!canReadDeliveries) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Deliveries</Heading>
          <Text color="gray">You do not have permission to read deliveries.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const deliveries = deliveriesQuery.data?.items ?? []
  const paging = deliveriesQuery.data

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Deliveries</Heading>

      <Card>
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
              <TextField.Root
                placeholder="Event ID"
                value={eventIdInput}
                onChange={(event) => setEventIdInput(event.target.value)}
              />
              <TextField.Root
                placeholder="Customer ID"
                value={customerIdInput}
                onChange={(event) => setCustomerIdInput(event.target.value)}
              />
              <TextField.Root
                placeholder="Contract ID"
                value={contractIdInput}
                onChange={(event) => setContractIdInput(event.target.value)}
              />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <Select.Root
                value={statusParam ?? 'all'}
                onValueChange={(value) =>
                  updateParams({ status: value === 'all' ? null : value, page: '1' })
                }
              >
                <Select.Trigger placeholder="Status" />
                <Select.Content>
                  <Select.Item value="all">All statuses</Select.Item>
                  <Select.Item value="Pending">Pending</Select.Item>
                  <Select.Item value="InFlight">InFlight</Select.Item>
                  <Select.Item value="Delivered">Delivered</Select.Item>
                  <Select.Item value="Failed">Failed</Select.Item>
                  <Select.Item value="Dead">Dead</Select.Item>
                </Select.Content>
              </Select.Root>

              <Select.Root
                value={destinationTypeParam ?? 'all'}
                onValueChange={(value) =>
                  updateParams({ destinationType: value === 'all' ? null : value, page: '1' })
                }
              >
                <Select.Trigger placeholder="Destination type" />
                <Select.Content>
                  <Select.Item value="all">All destinations</Select.Item>
                  {destinationValues.map((value) => (
                    <Select.Item key={value} value={value}>
                      {value}
                    </Select.Item>
                  ))}
                </Select.Content>
              </Select.Root>
            </Flex>

            <Flex wrap="wrap" gap="2" align="center">
              <Text size="2" color="gray">Created from</Text>
              <input
                type="datetime-local"
                value={createdFromParam}
                onChange={(event) =>
                  updateParams({ createdFrom: event.target.value || null, page: '1' })
                }
              />
              <Text size="2" color="gray">Created to</Text>
              <input
                type="datetime-local"
                value={createdToParam}
                onChange={(event) =>
                  updateParams({ createdTo: event.target.value || null, page: '1' })
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
                  setEventIdInput('')
                  setCustomerIdInput('')
                  setContractIdInput('')
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
          <Heading size="4">Delivery table</Heading>

          {deliveriesQuery.isLoading ? (
            <Text size="2" color="gray">Loading deliveries...</Text>
          ) : deliveriesQuery.isError ? (
            <Text size="2" color="red">
              {deliveriesQuery.error instanceof ApiError && deliveriesQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(deliveriesQuery.error, 'Unable to load deliveries.')}
            </Text>
          ) : deliveries.length === 0 ? (
            <Text size="2" color="gray">No deliveries found for current filters.</Text>
          ) : (
            <>
              <Table.Root size={preferences.denseTables ? '1' : '2'}>
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Event</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Destination</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('createdAt')}>
                        Created
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('updatedAt')}>
                        Updated
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {deliveries.map((delivery) => {
                    const highlighted = isFailedOrRetrying(delivery)
                    return (
                      <Table.Row
                        key={delivery.id}
                        style={
                          highlighted
                            ? {
                                backgroundColor: 'var(--amber-a3)',
                              }
                            : undefined
                        }
                      >
                        <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                        <Table.Cell>
                          <Badge
                            color={
                              delivery.status === 'Delivered'
                                ? 'green'
                                : delivery.status === 'Failed' || delivery.status === 'Dead'
                                  ? 'red'
                                  : 'amber'
                            }
                            variant="soft"
                          >
                            {formatDeliveryStatus(delivery.status)}
                          </Badge>
                        </Table.Cell>
                        <Table.Cell>{delivery.customerId}</Table.Cell>
                        <Table.Cell>{delivery.contractId}</Table.Cell>
                        <Table.Cell>{delivery.eventId}</Table.Cell>
                        <Table.Cell>{delivery.destinationType}</Table.Cell>
                        <Table.Cell>{new Date(delivery.createdAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>{delivery.attemptCount}</Table.Cell>
                        <Table.Cell>
                          <Button asChild size="1" variant="ghost">
                            <NavLink to={`/deliveries/${encodeURIComponent(delivery.id)}`}>
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
