import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Select,
  Table,
  Text,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText } from '../components/forms/form-feedback'
import { EntitySelect } from '../components/forms/entity-select'
import { EntityReference } from '../components/links/entity-reference'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { DetailAction, NavigableTableRow } from '../components/tables/table-actions'
import { formatDeliveryStatus } from '../features/deliveries/delivery-status'
import { deliveryIdError } from '../features/deliveries/delivery-filter-validation'
import { useDeliveriesQuery } from '../features/deliveries/use-deliveries'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type {
  DeliveryListItem,
  DeliveryListQuery,
  DeliveryStatusName,
  DestinationType,
} from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'
import { DataExportButtons } from '../components/exports/data-export-buttons'

type SortBy = 'createdAt' | 'updatedAt'
type SortDirection = 'asc' | 'desc'

const destinationValues: DestinationType[] = ['Http', 'Kafka', 'Nats', 'RabbitMq', 'Sqs']

function isFailedOrRetrying(item: DeliveryListItem) {
  const normalized = formatDeliveryStatus(item.status).toLowerCase()
  return normalized === 'failed' || normalized === 'inflight'
}

export function DeliveriesPage() {
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const defaultPageSize = 20

  const eventIdParam = searchParams.get('eventId') ?? ''
  const customerIdParam = searchParams.get('customerId') ?? ''
  const contractIdParam = searchParams.get('contractId') ?? ''
  const statusParam = (searchParams.get('status') as DeliveryStatusName | null) ?? undefined
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
  const [filterErrors, setFilterErrors] = useState({ eventId: null as string | null, contractId: null as string | null })
  const invalidQuery = !!deliveryIdError(eventIdParam, 'Event ID') || !!deliveryIdError(contractIdParam, 'Contract ID')

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

  const deliveriesQuery = useDeliveriesQuery(query, !invalidQuery)

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const errors = {
      eventId: deliveryIdError(eventIdInput, 'Event ID'),
      contractId: deliveryIdError(contractIdInput, 'Contract ID'),
    }
    setFilterErrors(errors)
    if (errors.eventId || errors.contractId) return

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
  const hasActiveFilters =
    eventIdParam.trim().length > 0 ||
    customerIdParam.trim().length > 0 ||
    contractIdParam.trim().length > 0 ||
    statusParam !== undefined ||
    destinationTypeParam !== undefined ||
    createdFromParam.trim().length > 0 ||
    createdToParam.trim().length > 0
  const paging = deliveriesQuery.data

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Deliveries</h1>
          <p className="page-subtitle">Track destination delivery status, retries, and processing history.</p>
        </div>
        <DataExportButtons entity="deliveries" filters={{ ...query }} disabled={invalidQuery} />
      </div>

      <Card className="operations-filter-card">
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
              <EntitySelect kind="event"
                aria-label="Event ID"
                aria-invalid={!!filterErrors.eventId}
                aria-describedby={filterErrors.eventId ? 'delivery-event-id-error' : undefined}
                placeholder="Event ID"
                value={eventIdInput}
                onChange={(value) => {
                  setEventIdInput(value)
                  setFilterErrors((previous) => ({ ...previous, eventId: null }))
                }}
              />
              <FieldErrorText id="delivery-event-id-error" message={filterErrors.eventId} />
              <EntitySelect kind="customer"
                aria-label="Customer ID"
                placeholder="Customer ID"
                value={customerIdInput}
                onChange={setCustomerIdInput}
              />
              <EntitySelect kind="contract"
                aria-label="Contract ID"
                aria-invalid={!!filterErrors.contractId}
                aria-describedby={filterErrors.contractId ? 'delivery-contract-id-error' : undefined}
                placeholder="Contract ID"
                value={contractIdInput}
                onChange={(value) => {
                  setContractIdInput(value)
                  setFilterErrors((previous) => ({ ...previous, contractId: null }))
                }}
              />
              <FieldErrorText id="delivery-contract-id-error" message={filterErrors.contractId} />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <Select.Root
                value={statusParam ?? 'all'}
                onValueChange={(value) =>
                  updateParams({ status: value === 'all' ? null : value, page: '1' })
                }
              >
                <Select.Trigger placeholder="Status" aria-label="Delivery status" />
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
                <Select.Trigger placeholder="Destination type" aria-label="Destination type" />
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
                aria-label="Created from"
                type="datetime-local"
                value={createdFromParam}
                onChange={(event) =>
                  updateParams({ createdFrom: event.target.value || null, page: '1' })
                }
              />
              <Text size="2" color="gray">Created to</Text>
              <input
                aria-label="Created to"
                type="datetime-local"
                value={createdToParam}
                onChange={(event) =>
                  updateParams({ createdTo: event.target.value || null, page: '1' })
                }
              />
            </Flex>

            <Flex gap="2" wrap="wrap" className="filter-actions">
              <Button type="submit">Apply filters</Button>
              <Select.Root
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

          </Flex>
        </form>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Delivery table</Heading>

          <ListFeedback
            isLoading={deliveriesQuery.isLoading}
            isError={deliveriesQuery.isError}
            error={deliveriesQuery.error}
            isEmpty={deliveries.length === 0}
            loadingMessage="Loading deliveries..."
            errorMessage={
              deliveriesQuery.error instanceof ApiError && deliveriesQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(deliveriesQuery.error, 'Unable to load deliveries.')
            }
            emptyMessage="No deliveries found for current filters."
            emptyTitle={hasActiveFilters ? 'No deliveries match current filters' : 'No deliveries yet'}
            emptyDescription={
              hasActiveFilters
                ? 'Try broadening filters to find matching deliveries.'
                : 'Deliveries will appear here after events are matched to active contracts.'
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
                    void deliveriesQuery.refetch()
                  },
                }
            }
          >
            <>
              <div className="responsive-table">
                <Table.Root size="2" aria-label="Deliveries table">
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
                      const displayStatus = formatDeliveryStatus(delivery.status)

                      return (
                        <NavigableTableRow
                          key={delivery.id}
                          to={`/deliveries/${encodeURIComponent(delivery.id)}`}
                          label={`delivery ${delivery.id}`}
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
                                displayStatus === 'Delivered'
                                  ? 'green'
                                  : displayStatus === 'Failed' || displayStatus === 'Dead'
                                    ? 'red'
                                    : 'amber'
                              }
                              variant="soft"
                            >
                              {displayStatus}
                            </Badge>
                          </Table.Cell>
                          <Table.Cell><EntityReference kind="customer" id={delivery.customerId} /></Table.Cell>
                          <Table.Cell><EntityReference kind="contract" id={delivery.contractId} /></Table.Cell>
                          <Table.Cell><EntityReference kind="event" id={delivery.eventId} label={delivery.eventType} /></Table.Cell>
                          <Table.Cell>{delivery.destinationType}</Table.Cell>
                          <Table.Cell>{new Date(delivery.createdAt).toLocaleString()}</Table.Cell>
                          <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                          <Table.Cell>{delivery.attemptCount}</Table.Cell>
                          <Table.Cell>
                            <div className="table-row-actions">
                              <DetailAction
                                to={`/deliveries/${encodeURIComponent(delivery.id)}`}
                                label="Open delivery"
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
