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
import { NavLink, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useFailedDeliveriesQuery,
  useRetryDeliveryMutation,
} from '../features/deliveries/use-deliveries'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type {
  DeliveryListItem,
  DeliveryListQuery,
} from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'createdAt' | 'updatedAt'
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

function formatFailureCategory(item: DeliveryListItem) {
  return item.latestFailure?.failureCategory ?? 'unknown'
}

export function FailedDeliveriesPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadFailures = hasPermission(currentUser, 'delivery-failures.read')
  const canOperateDeliveries = hasPermission(currentUser, 'deliveries.operate')

  const customerIdParam = searchParams.get('customerId') ?? ''
  const contractIdParam = searchParams.get('contractId') ?? ''
  const failureCategoryParam = searchParams.get('failureCategory') ?? ''
  const statusParam = searchParams.get('status') ?? 'Failed'
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('updatedAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ?? ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), 20)

  const [customerIdInput, setCustomerIdInput] = useState(customerIdParam)
  const [contractIdInput, setContractIdInput] = useState(contractIdParam)
  const [failureCategoryInput, setFailureCategoryInput] = useState(failureCategoryParam)
  const [retryReason, setRetryReason] = useState('')
  const [confirmDeliveryId, setConfirmDeliveryId] = useState<string | null>(null)

  const query = useMemo<DeliveryListQuery>(
    () => ({
      customerId: customerIdParam,
      contractId: contractIdParam,
      failureCategory: failureCategoryParam,
      status: statusParam === 'Dead' ? 'Dead' : 'Failed',
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

  const failedQuery = useFailedDeliveriesQuery(query)

  const activeRetryMutation = useRetryDeliveryMutation(confirmDeliveryId ?? '')

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

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

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

    if (!window.confirm('Retry this delivery now? This may trigger immediate redelivery attempts.')) {
      return
    }

    setConfirmDeliveryId(deliveryId)

    try {
      await activeRetryMutation.mutateAsync({
        operationId: crypto.randomUUID(),
        reason: retryReason.trim() || null,
      })
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
      <Heading size="6">Failed & dead deliveries</Heading>

      <Card>
        <form onSubmit={handleSearchSubmit}>
          <Flex direction="column" gap="3">
            <Heading size="4">Filters</Heading>

            <Flex wrap="wrap" gap="2">
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
              <TextField.Root
                placeholder="Failure type/category"
                value={failureCategoryInput}
                onChange={(event) => setFailureCategoryInput(event.target.value)}
              />
            </Flex>

            <Flex wrap="wrap" gap="2">
              <Select.Root
                value={statusParam}
                onValueChange={(value) =>
                  updateParams({ status: value, page: '1' })
                }
              >
                <Select.Trigger />
                <Select.Content>
                  <Select.Item value="Failed">Failed only</Select.Item>
                  <Select.Item value="Dead">Dead only</Select.Item>
                </Select.Content>
              </Select.Root>

              <TextField.Root
                placeholder="Retry reason (optional)"
                value={retryReason}
                onChange={(event) => setRetryReason(event.target.value)}
                disabled={!canOperateDeliveries}
              />
            </Flex>

            <Flex gap="2">
              <Button type="submit">Apply filters</Button>
              <Button
                type="button"
                variant="soft"
                onClick={() => {
                  setCustomerIdInput('')
                  setContractIdInput('')
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

          {failedQuery.isLoading ? (
            <Text size="2" color="gray">Loading failed deliveries...</Text>
          ) : failedQuery.isError ? (
            <Text size="2" color="red">
              {failedQuery.error instanceof ApiError && failedQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(failedQuery.error, 'Unable to load failed deliveries.')}
            </Text>
          ) : deliveries.length === 0 ? (
            <Text size="2" color="gray">No failed/dead deliveries found for current filters.</Text>
          ) : (
            <>
              <Table.Root>
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
                    return (
                      <Table.Row key={delivery.id}>
                        <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                        <Table.Cell>
                          <Badge color={delivery.status === 'Dead' ? 'red' : 'amber'} variant="soft">
                            {delivery.status}
                          </Badge>
                        </Table.Cell>
                        <Table.Cell>{delivery.customerId}</Table.Cell>
                        <Table.Cell>{delivery.contractId}</Table.Cell>
                        <Table.Cell>{formatFailureCategory(delivery)}</Table.Cell>
                        <Table.Cell>{delivery.latestFailure?.error ?? delivery.lastError ?? '-'}</Table.Cell>
                        <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>
                          <Flex gap="2">
                            <Button asChild size="1" variant="ghost">
                              <NavLink to={`/deliveries/${encodeURIComponent(delivery.id)}`}>
                                Detail
                              </NavLink>
                            </Button>
                            <Button
                              size="1"
                              disabled={!canOperateDeliveries || retrying}
                              onClick={() => {
                                void handleRetry(delivery.id)
                              }}
                            >
                              {retrying ? 'Retrying...' : 'Retry'}
                            </Button>
                          </Flex>
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
