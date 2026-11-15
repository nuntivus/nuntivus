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
import { ErrorPreview } from '../components/errors/error-preview'
import { DataExportButtons } from '../components/exports/data-export-buttons'
import { FieldErrorText } from '../components/forms/form-feedback'
import { EntitySelect } from '../components/forms/entity-select'
import { EntityReference } from '../components/links/entity-reference'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { RetryIcon } from '../components/icons/nav-icons'
import { deliveryIdError } from '../features/deliveries/delivery-filter-validation'
import { formatDeliveryStatus } from '../features/deliveries/delivery-status'
import { DetailIndicator, NavigableTableRow, TableIconButton } from '../components/tables/table-actions'
import {
  useFailedDeliveriesQuery,
  useRetryDeliveryMutation,
} from '../features/deliveries/use-deliveries'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type {
  DeliveryListItem,
  DeliveryListQuery,
} from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'createdAt' | 'updatedAt'
type SortDirection = 'asc' | 'desc'

function formatFailureCategory(item: DeliveryListItem) {
  return item.latestFailure?.failureCategory ?? 'unknown'
}

export function FailedDeliveriesPage() {
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadFailures = hasPermission(currentUser, 'delivery-failures.read')
  const canOperateDeliveries = hasPermission(currentUser, 'deliveries.operate')

  const customerIdParam = searchParams.get('customerId') ?? ''
  const contractIdParam = searchParams.get('contractId') ?? ''
  const failureCategoryParam = searchParams.get('failureCategory') ?? ''
  const requestedStatus = searchParams.get('status')
  const statusParam = requestedStatus === 'Failed' || requestedStatus === 'Dead' ? requestedStatus : 'all'
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('updatedAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ?? ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), 20)

  const [customerIdInput, setCustomerIdInput] = useState(customerIdParam)
  const [contractIdInput, setContractIdInput] = useState(contractIdParam)
  const [contractIdError, setContractIdError] = useState<string | null>(null)
  const [failureCategoryInput, setFailureCategoryInput] = useState(failureCategoryParam)
  const [retryReason, setRetryReason] = useState('')
  const [confirmDeliveryId, setConfirmDeliveryId] = useState<string | null>(null)

  const query = useMemo<DeliveryListQuery>(
    () => ({
      customerId: customerIdParam,
      contractId: contractIdParam,
      failureCategory: failureCategoryParam,
      status: statusParam === 'all' ? undefined : statusParam,
      failuresOnly: true,
      sortBy: sortByParam,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [
      contractIdParam,
      customerIdParam,
      failureCategoryParam,
      pageParam,
      pageSizeParam,
      sortByParam,
      sortDirectionParam,
      statusParam,
    ],
  )

  const failedQuery = useFailedDeliveriesQuery(query, !deliveryIdError(contractIdParam, 'Contract ID'))

  const activeRetryMutation = useRetryDeliveryMutation(confirmDeliveryId ?? '')

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const error = deliveryIdError(contractIdInput, 'Contract ID')
    setContractIdError(error)
    if (error) return

    updateParams({
      customerId: customerIdInput.trim() || null,
      contractId: contractIdInput.trim() || null,
      failureCategory: failureCategoryInput.trim() || null,
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

  async function handleRetry(deliveryId: string) {
    if (!canOperateDeliveries) {
      return
    }

    const confirmed = await confirm({
      title: 'Retry delivery',
      description:
        'Retry this delivery now? This may trigger immediate redelivery attempts and can duplicate downstream processing.',
      confirmText: 'Retry now',
      cancelText: 'Cancel',
      confirmColor: 'amber',
    })

    if (!confirmed) {
      return
    }

    setConfirmDeliveryId(deliveryId)

    try {
      await activeRetryMutation.mutateAsync({
        operationId: crypto.randomUUID(),
        reason: retryReason.trim() || null,
      })
      notifications.success('Delivery retry requested.', 'Retry delivery')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to retry delivery.'), 'Retry delivery')
    } finally {
      setConfirmDeliveryId(null)
      setRetryReason('')
    }
  }

  if (!canReadFailures) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Failed deliveries</Heading>
          <Text color="gray">You do not have permission to read delivery failures.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const deliveries = failedQuery.data?.items ?? []
  const paging = failedQuery.data

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Failed &amp; dead deliveries</h1>
          <p className="page-subtitle">Investigate delivery errors and retry recoverable failures.</p>
        </div>
      </div>

      <Card className="operations-filter-card">
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
              <EntitySelect kind="customer"
                aria-label="Customer ID"
                placeholder="Customer ID"
                value={customerIdInput}
                onChange={setCustomerIdInput}
              />
              <EntitySelect kind="contract"
                aria-label="Contract ID"
                aria-invalid={!!contractIdError}
                aria-describedby={contractIdError ? 'failed-contract-id-error' : undefined}
                placeholder="Contract ID"
                value={contractIdInput}
                onChange={(value) => { setContractIdInput(value); setContractIdError(null) }}
              />
              <FieldErrorText id="failed-contract-id-error" message={contractIdError} />
              <TextField.Root
                aria-label="Failure type or category"
                placeholder="Failure type/category"
                value={failureCategoryInput}
                onChange={(event) => setFailureCategoryInput(event.target.value)}
              />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <Select.Root
                value={statusParam}
                onValueChange={(value) =>
                  updateParams({ status: value === 'all' ? null : value, page: '1' })
                }
              >
                <Select.Trigger aria-label="Failure status" />
                <Select.Content>
                  <Select.Item value="all">Failed &amp; dead</Select.Item>
                  <Select.Item value="Failed">Failed only</Select.Item>
                  <Select.Item value="Dead">Dead only</Select.Item>
                </Select.Content>
              </Select.Root>

              <TextField.Root
                aria-label="Retry reason"
                placeholder="Retry reason (optional)"
                value={retryReason}
                onChange={(event) => setRetryReason(event.target.value)}
                disabled={!canOperateDeliveries}
              />
            </Flex>

            <Flex gap="2" wrap="wrap" className="filter-actions">
              <Button type="submit">Apply filters</Button>
              <Button
                type="button"
                variant="soft"
                onClick={() => {
                  setCustomerIdInput('')
                  setContractIdInput('')
                  setContractIdError(null)
                  setFailureCategoryInput('')
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
          <Flex align="center" justify="between" wrap="wrap" gap="2">
            <Heading size="4">Failures</Heading>
            <Button
              variant="soft"
              disabled
              title="Bulk retry will be enabled when backend batch operation endpoint is available."
            >
              Bulk retry (later)
            </Button>
          </Flex>

          <DataExportButtons entity="deliveries" filters={{ ...query }} disabled={!!deliveryIdError(contractIdParam, 'Contract ID')} />

          <ListFeedback
            isLoading={failedQuery.isLoading}
            isError={failedQuery.isError}
            error={failedQuery.error}
            isEmpty={deliveries.length === 0}
            loadingMessage="Loading failed deliveries..."
            errorMessage={
              failedQuery.error instanceof ApiError && failedQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(failedQuery.error, 'Unable to load failed deliveries.')
            }
            emptyMessage="No failed/dead deliveries found for current filters."
          >
            <>
              <div className="responsive-table failures-table">
                <Table.Root size="1" aria-label="Failed deliveries table">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Failure type</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Last failure</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>
                        <Button variant="ghost" onClick={() => handleSort('updatedAt')}>
                          Updated
                        </Button>
                      </Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {deliveries.map((delivery) => {
                      const retrying = activeRetryMutation.isPending && confirmDeliveryId === delivery.id
                      const status = formatDeliveryStatus(delivery.status)
                      return (
                        <NavigableTableRow
                          key={delivery.id}
                          to={`/deliveries/${encodeURIComponent(delivery.id)}`}
                          label={`delivery ${delivery.id}`}
                        >
                          <Table.RowHeaderCell><span className="failure-cell-text" title={delivery.id}>{delivery.id}</span></Table.RowHeaderCell>
                          <Table.Cell>
                            <Badge color={status === 'Dead' ? 'red' : 'amber'} variant="soft" title={status === 'Dead' ? 'No automatic retry' : 'Retry may be scheduled'}>
                              {status}
                            </Badge>
                          </Table.Cell>
                          <Table.Cell><span className="failure-cell-text"><EntityReference kind="customer" id={delivery.customerId} /></span></Table.Cell>
                          <Table.Cell><span className="failure-cell-text"><EntityReference kind="contract" id={delivery.contractId} /></span></Table.Cell>
                          <Table.Cell>{formatFailureCategory(delivery)}</Table.Cell>
                          <Table.Cell><ErrorPreview error={delivery.latestFailure?.error ?? delivery.lastError} /></Table.Cell>
                          <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                          <Table.Cell>
                            <div className="table-row-actions">
                              <TableIconButton
                                label={retrying ? 'Retrying delivery' : 'Retry delivery'}
                                disabled={!canOperateDeliveries || retrying}
                                onClick={() => {
                                  void handleRetry(delivery.id)
                                }}
                                color="amber"
                              >
                                <RetryIcon width={15} height={15} />
                              </TableIconButton>
                              <DetailIndicator />
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

          {activeRetryMutation.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(activeRetryMutation.error, 'Unable to retry delivery.')}
            </Text>
          ) : null}

          <Text size="2" color="gray">
            Destructive actions require explicit confirmation dialogs to avoid accidental operations.
          </Text>
        </Flex>
      </Card>
    </Flex>
  )
}
