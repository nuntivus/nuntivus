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
import { hasPermission } from '../auth/permissions'
import { JsonViewer } from '../components/json/json-viewer'
import { EyeIcon } from '../components/icons/nav-icons'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { TableIconButton } from '../components/tables/table-actions'
import { useAuditLogsQuery } from '../features/audit-logs/use-audit-logs'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type {
  AuditLogListItem,
  AuditLogListQuery,
} from '../services/api/audit-logs-api'
import { useAppSelector } from '../store/hooks'

type SortDirection = 'asc' | 'desc'

function formatTitleCase(value: string) {
  return value
    .replace(/[-_]/g, ' ')
    .split(' ')
    .filter((part) => part.length > 0)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join(' ')
}

function safeParseJson(value: string): unknown {
  try {
    return JSON.parse(value)
  } catch {
    return { raw: value }
  }
}

function rowColor(category: string) {
  switch (category.toLowerCase()) {
    case 'delivery':
      return 'amber'
    case 'settings':
      return 'violet'
    case 'contract':
      return 'cyan'
    case 'customer':
      return 'indigo'
    case 'event':
      return 'green'
    case 'admin-user':
      return 'orange'
    default:
      return 'gray'
  }
}

function toShort(value: string | null) {
  if (!value) {
    return '-'
  }

  return value.length > 12 ? `${value.slice(0, 12)}…` : value
}

function summarizeDetails(item: AuditLogListItem) {
  const payload = safeParseJson(item.details)
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
    return item.details.length > 80 ? `${item.details.slice(0, 80)}…` : item.details
  }

  const objectPayload = payload as Record<string, unknown>

  if (typeof objectPayload.reason === 'string' && objectPayload.reason.trim()) {
    return objectPayload.reason
  }

  if (typeof objectPayload.operation === 'string' && objectPayload.operation.trim()) {
    return objectPayload.operation
  }

  if (typeof objectPayload.message === 'string' && objectPayload.message.trim()) {
    return objectPayload.message
  }

  const compact = JSON.stringify(objectPayload)
  return compact.length > 80 ? `${compact.slice(0, 80)}…` : compact
}

export function AuditLogsPage() {
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const { currentUser } = useAppSelector((state) => state.auth)
  const canReadAuditLogs = hasPermission(currentUser, 'audit-logs.read')

  const categoryParam = searchParams.get('category') ?? ''
  const actionParam = searchParams.get('action') ?? ''
  const actorIdParam = searchParams.get('actorId') ?? ''
  const customerIdParam = searchParams.get('customerId') ?? ''
  const contractIdParam = searchParams.get('contractId') ?? ''
  const deliveryIdParam = searchParams.get('deliveryId') ?? ''
  const eventIdParam = searchParams.get('eventId') ?? ''
  const operationIdParam = searchParams.get('operationId') ?? ''
  const createdFromParam = searchParams.get('createdFrom') ?? ''
  const createdToParam = searchParams.get('createdTo') ?? ''
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ?? ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), 20)

  const [categoryInput, setCategoryInput] = useState(categoryParam)
  const [actionInput, setActionInput] = useState(actionParam)
  const [actorIdInput, setActorIdInput] = useState(actorIdParam)
  const [customerIdInput, setCustomerIdInput] = useState(customerIdParam)
  const [contractIdInput, setContractIdInput] = useState(contractIdParam)
  const [deliveryIdInput, setDeliveryIdInput] = useState(deliveryIdParam)
  const [eventIdInput, setEventIdInput] = useState(eventIdParam)
  const [operationIdInput, setOperationIdInput] = useState(operationIdParam)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const query = useMemo<AuditLogListQuery>(
    () => ({
      category: categoryParam,
      action: actionParam,
      actorId: actorIdParam,
      customerId: customerIdParam,
      contractId: contractIdParam,
      deliveryId: deliveryIdParam,
      eventId: eventIdParam,
      operationId: operationIdParam,
      createdFrom: createdFromParam || undefined,
      createdTo: createdToParam || undefined,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [
      actionParam,
      actorIdParam,
      categoryParam,
      contractIdParam,
      createdFromParam,
      createdToParam,
      customerIdParam,
      deliveryIdParam,
      eventIdParam,
      operationIdParam,
      pageParam,
      pageSizeParam,
      sortDirectionParam,
    ],
  )

  const auditLogsQuery = useAuditLogsQuery(query)

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    updateParams({
      category: categoryInput.trim() || null,
      action: actionInput.trim() || null,
      actorId: actorIdInput.trim() || null,
      customerId: customerIdInput.trim() || null,
      contractId: contractIdInput.trim() || null,
      deliveryId: deliveryIdInput.trim() || null,
      eventId: eventIdInput.trim() || null,
      operationId: operationIdInput.trim() || null,
      page: '1',
    })
  }

  if (!canReadAuditLogs) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Audit logs</Heading>
          <Text color="gray">You do not have permission to read audit logs.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const rows = auditLogsQuery.data?.items ?? []
  const paging = auditLogsQuery.data

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Audit logs</h1>
          <p className="page-subtitle">Review administrative changes and trace platform operations.</p>
        </div>
      </div>

      <Card className="operations-filter-card">
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
              <TextField.Root
                aria-label="Category"
                placeholder="Category (contract, customer, delivery...)"
                value={categoryInput}
                onChange={(event) => setCategoryInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Action"
                placeholder="Action"
                value={actionInput}
                onChange={(event) => setActionInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Actor"
                placeholder="Actor"
                value={actorIdInput}
                onChange={(event) => setActorIdInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Customer ID"
                placeholder="Customer ID"
                value={customerIdInput}
                onChange={(event) => setCustomerIdInput(event.target.value)}
              />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <TextField.Root
                aria-label="Contract ID"
                placeholder="Contract ID"
                value={contractIdInput}
                onChange={(event) => setContractIdInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Delivery ID"
                placeholder="Delivery ID"
                value={deliveryIdInput}
                onChange={(event) => setDeliveryIdInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Event ID"
                placeholder="Event ID"
                value={eventIdInput}
                onChange={(event) => setEventIdInput(event.target.value)}
              />
              <TextField.Root
                aria-label="Operation ID"
                placeholder="Operation ID"
                value={operationIdInput}
                onChange={(event) => setOperationIdInput(event.target.value)}
              />
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
              <Select.Root
                value={sortDirectionParam}
                onValueChange={(value) => updateParams({ sortDirection: value, page: '1' })}
              >
                <Select.Trigger aria-label="Sort direction" />
                <Select.Content>
                  <Select.Item value="desc">Newest first</Select.Item>
                  <Select.Item value="asc">Oldest first</Select.Item>
                </Select.Content>
              </Select.Root>

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

              <Button type="submit">Apply filters</Button>

              <Button
                type="button"
                variant="soft"
                onClick={() => {
                  setCategoryInput('')
                  setActionInput('')
                  setActorIdInput('')
                  setCustomerIdInput('')
                  setContractIdInput('')
                  setDeliveryIdInput('')
                  setEventIdInput('')
                  setOperationIdInput('')
                  setExpandedId(null)
                  setSearchParams(new URLSearchParams())
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
          <Heading size="4">Audit timeline</Heading>

          <ListFeedback
            isLoading={auditLogsQuery.isLoading}
            isError={auditLogsQuery.isError}
            error={auditLogsQuery.error}
            isEmpty={rows.length === 0}
            loadingMessage="Loading audit logs..."
            errorMessage={
              auditLogsQuery.error instanceof ApiError && auditLogsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(auditLogsQuery.error, 'Unable to load audit logs.')
            }
            emptyMessage="No audit logs found for current filters."
          >
            <>
              <div className="responsive-table">
                <Table.Root aria-label="Audit logs table">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>Time</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Category</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Action</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Actor</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Delivery</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Summary</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Details</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {rows.map((item) => {
                      const expanded = expandedId === item.id

                      return (
                        <Table.Row key={item.id}>
                          <Table.Cell>{new Date(item.createdAt).toLocaleString()}</Table.Cell>
                          <Table.Cell>
                            <Badge color={rowColor(item.category)} variant="soft">
                              {item.category}
                            </Badge>
                          </Table.Cell>
                          <Table.Cell>{formatTitleCase(item.action)}</Table.Cell>
                          <Table.Cell>{item.actorId ?? '-'}</Table.Cell>
                          <Table.Cell>{item.customerId ?? '-'}</Table.Cell>
                          <Table.Cell title={item.contractId ?? undefined}>{toShort(item.contractId)}</Table.Cell>
                          <Table.Cell title={item.deliveryId ?? undefined}>{toShort(item.deliveryId)}</Table.Cell>
                          <Table.Cell>{summarizeDetails(item)}</Table.Cell>
                          <Table.Cell>
                            <TableIconButton
                              label={expanded ? 'Hide audit payload' : 'View audit payload'}
                              onClick={() => setExpandedId(expanded ? null : item.id)}
                            >
                              <EyeIcon width={15} height={15} />
                            </TableIconButton>
                          </Table.Cell>
                        </Table.Row>
                      )
                    })}
                  </Table.Body>
                </Table.Root>
              </div>

              {expandedId ? (
                <Card>
                  <Flex direction="column" gap="2">
                    <Text size="2" weight="medium">Audit payload</Text>
                    <JsonViewer
                      value={safeParseJson(rows.find((item) => item.id === expandedId)?.details ?? '{}')}
                      defaultCollapsed={false}
                      copyLabel="Copy details"
                      searchPlaceholder="Search audit payload"
                    />
                  </Flex>
                </Card>
              ) : null}

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