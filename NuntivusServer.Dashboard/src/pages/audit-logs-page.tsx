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
import { useMemo, useRef, useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { JsonViewer } from '../components/json/json-viewer'
import { FormDialog } from '../components/dialogs/form-dialog'
import { EntityReference } from '../components/links/entity-reference'
import { EntitySelect } from '../components/forms/entity-select'
import { EyeIcon } from '../components/icons/nav-icons'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { DetailIndicator, NavigableTableRow, TableIconButton } from '../components/tables/table-actions'
import { useAuditLogsQuery } from '../features/audit-logs/use-audit-logs'
import { auditPreviewFields, parseAuditDetails } from '../features/audit-logs/audit-details'
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
  const [selectedAudit, setSelectedAudit] = useState<AuditLogListItem | null>(null)
  const auditTrigger = useRef<HTMLElement | null>(null)
  const selectedPayload = useMemo(() => selectedAudit ? parseAuditDetails(selectedAudit.details) : null, [selectedAudit])

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

  function openAudit(item: AuditLogListItem) {
    auditTrigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setSelectedAudit(item)
  }

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
              <EntitySelect kind="customer"
                aria-label="Customer ID"
                placeholder="Customer ID"
                value={customerIdInput}
                onChange={setCustomerIdInput}
              />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <EntitySelect kind="contract"
                aria-label="Contract ID"
                placeholder="Contract ID"
                value={contractIdInput}
                onChange={setContractIdInput}
              />
              <EntitySelect kind="delivery"
                aria-label="Delivery ID"
                placeholder="Delivery ID"
                value={deliveryIdInput}
                onChange={setDeliveryIdInput}
              />
              <EntitySelect kind="event"
                aria-label="Event ID"
                placeholder="Event ID"
                value={eventIdInput}
                onChange={setEventIdInput}
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
                  setSelectedAudit(null)
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
              <div className="responsive-table audit-table">
                <Table.Root aria-label="Audit logs table" size="1">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>Time</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Category</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Action</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Actor</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Delivery</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Data preview</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Details</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {rows.map((item) => {
                      const preview = auditPreviewFields(parseAuditDetails(item.details))

                      return (
                        <NavigableTableRow key={item.id} to={item.deliveryId ? `/deliveries/${encodeURIComponent(item.deliveryId)}` : undefined} label={`delivery ${item.deliveryId}`}>
                          <Table.Cell><span className="audit-cell-text" title={new Date(item.createdAt).toLocaleString()}>{new Date(item.createdAt).toLocaleString()}</span></Table.Cell>
                          <Table.Cell>
                            <Badge color={rowColor(item.category)} variant="soft">
                              {item.category}
                            </Badge>
                          </Table.Cell>
                          <Table.Cell><span className="audit-cell-text" title={formatTitleCase(item.action)}>{formatTitleCase(item.action)}</span></Table.Cell>
                          <Table.Cell><span className="audit-cell-text" title={item.actorId ?? undefined}>{item.actorId ?? '-'}</span></Table.Cell>
                          <Table.Cell><span className="audit-cell-text"><EntityReference kind="customer" id={item.customerId} /></span></Table.Cell>
                          <Table.Cell><span className="audit-cell-text"><EntityReference kind="contract" id={item.contractId} /></span></Table.Cell>
                          <Table.Cell title={item.deliveryId ?? undefined}>
                            {item.deliveryId ? toShort(item.deliveryId) : '-'}
                          </Table.Cell>
                          <Table.Cell>
                            <button type="button" className="audit-data-preview" aria-label="Preview audit data" onClick={() => openAudit(item)}>
                              {preview.length ? preview.map((field, index) => <span key={`${index}-${field.label}`}>
                                {index > 0 ? ' · ' : null}<strong>{field.label}:</strong> {field.value}
                              </span>) : 'No payload fields'}
                            </button>
                          </Table.Cell>
                          <Table.Cell>
                            <div className="table-row-actions"><TableIconButton
                              label="View audit payload"
                              onClick={() => openAudit(item)}
                            >
                              <EyeIcon width={15} height={15} />
                            </TableIconButton>
                            {item.deliveryId && <DetailIndicator />}</div>
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
      <FormDialog
        open={selectedAudit !== null}
        onOpenChange={(open) => { if (!open) setSelectedAudit(null) }}
        title="Audit log details"
        description="Record information and structured operation data."
        descriptionId="audit-details-description"
        maxWidth="960px"
        onCloseAutoFocus={(event) => { event.preventDefault(); auditTrigger.current?.focus() }}
        footer={<Button type="button" variant="soft" onClick={() => setSelectedAudit(null)}>Close</Button>}
      >
        {selectedAudit ? <Flex direction="column" gap="4" className="audit-details-content">
          <dl className="audit-details-metadata">
            <div><dt>Audit ID</dt><dd>{selectedAudit.id}</dd></div>
            <div><dt>Time</dt><dd>{new Date(selectedAudit.createdAt).toLocaleString()}</dd></div>
            <div><dt>Category</dt><dd>{selectedAudit.category}</dd></div>
            <div><dt>Action</dt><dd>{formatTitleCase(selectedAudit.action)}</dd></div>
            <div><dt>Actor</dt><dd>{selectedAudit.actorId ?? '-'}</dd></div>
            <div><dt>Operation ID</dt><dd>{selectedAudit.operationId ?? '-'}</dd></div>
            <div><dt>Customer</dt><dd><EntityReference kind="customer" id={selectedAudit.customerId} /></dd></div>
            <div><dt>Contract</dt><dd><EntityReference kind="contract" id={selectedAudit.contractId} /></dd></div>
            <div><dt>Delivery ID</dt><dd>{selectedAudit.deliveryId ? <NavLink to={`/deliveries/${encodeURIComponent(selectedAudit.deliveryId)}`}>{selectedAudit.deliveryId}</NavLink> : '-'}</dd></div>
            <div><dt>Event ID</dt><dd>{selectedAudit.eventId ? <NavLink to={`/events/${encodeURIComponent(selectedAudit.eventId)}`}>{selectedAudit.eventId}</NavLink> : '-'}</dd></div>
            <div><dt>Expires</dt><dd>{selectedAudit.expiresAt ? new Date(selectedAudit.expiresAt).toLocaleString() : '-'}</dd></div>
          </dl>
          <Heading size="3">Audit payload</Heading>
          <JsonViewer value={selectedPayload} defaultCollapsed={false} maxHeight={420}
            copyLabel="Copy details" searchPlaceholder="Search audit payload" />
        </Flex> : null}
      </FormDialog>
    </Flex>
  )
}