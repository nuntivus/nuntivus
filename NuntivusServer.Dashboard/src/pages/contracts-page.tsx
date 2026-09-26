import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Link,
  Select,
  Table,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { SearchIcon } from '../components/icons/nav-icons'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import {
  useContractsQuery,
  useDisableContractMutation,
  useEnableContractMutation,
} from '../features/contracts/use-contracts'
import { useBulkSelection } from '../hooks/use-bulk-selection'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import {
  parseBooleanParam,
  parsePositiveInt,
  useUrlQueryParams,
} from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { ContractListQuery, DestinationType } from '../services/api/contracts-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'createdAt' | 'updatedAt' | 'name' | 'customerId' | 'destinationType'
type SortDirection = 'asc' | 'desc'

const destinationValues: DestinationType[] = [
  'Http',
  'Kafka',
  'Nats',
  'RabbitMq',
  'Sqs',
]

export function ContractsPage() {
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')
  const defaultPageSize = 20

  const searchParam = searchParams.get('search') ?? ''
  const customerIdParam = searchParams.get('customerId') ?? ''
  const enabledParam = parseBooleanParam(searchParams.get('enabled'))
  const destinationTypeParam =
    (searchParams.get('destinationType') as DestinationType | null) ?? undefined
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('updatedAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ??
    ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), defaultPageSize)

  const query = useMemo<ContractListQuery>(
    () => ({
      search: searchParam,
      customerId: customerIdParam,
      enabled: enabledParam,
      destinationType: destinationTypeParam,
      sortBy: sortByParam,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [
      customerIdParam,
      destinationTypeParam,
      enabledParam,
      pageParam,
      pageSizeParam,
      searchParam,
      sortByParam,
      sortDirectionParam,
    ],
  )

  const contractsQuery = useContractsQuery(query)
  const enableContractMutation = useEnableContractMutation()
  const disableContractMutation = useDisableContractMutation()

  const [searchInput, setSearchInput] = useState(searchParam)
  const [rowActionId, setRowActionId] = useState<string | null>(null)
  const [bulkActionPending, setBulkActionPending] = useState(false)
  const [bulkActionError, setBulkActionError] = useState<string | null>(null)

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    updateParams({
      search: searchInput.trim() || null,
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

  async function handleToggleEnabled(id: string, enabled: boolean) {
    if (!canWriteContracts) {
      return
    }

    if (enabled) {
      const confirmed = await confirm({
        title: 'Disable contract',
        description:
          'Disable this contract? Event matching can stop for this contract until it is enabled again.',
        confirmText: 'Disable contract',
        cancelText: 'Cancel',
        confirmColor: 'red',
      })

      if (!confirmed) {
        return
      }
    }

    setRowActionId(id)

    try {
      if (enabled) {
        await disableContractMutation.mutateAsync(id)
        notifications.success(`Contract ${id} disabled.`, 'Contract updated')
      } else {
        await enableContractMutation.mutateAsync(id)
        notifications.success(`Contract ${id} enabled.`, 'Contract updated')
      }
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to update contract.'), 'Contract updated')
    } finally {
      setRowActionId(null)
    }
  }

  const contracts = useMemo(() => contractsQuery.data?.items ?? [], [contractsQuery.data?.items])
  const paging = contractsQuery.data
  const hasActiveFilters =
    searchParam.trim().length > 0 ||
    customerIdParam.trim().length > 0 ||
    enabledParam !== undefined ||
    destinationTypeParam !== undefined
  const visibleIds = useMemo(() => contracts.map((contract) => contract.id), [contracts])
  const {
    selectedIds,
    selectedCount,
    allVisibleSelected,
    toggleSelection,
    toggleVisibleSelection,
    clearSelection,
    isSelected,
  } = useBulkSelection(visibleIds)

  async function handleBulkToggle(enabled: boolean) {
    if (!canWriteContracts || selectedIds.length === 0) {
      return
    }

    if (enabled) {
      const confirmed = await confirm({
        title: 'Disable selected contracts',
        description: `Disable ${selectedIds.length} selected contract${selectedIds.length === 1 ? '' : 's'}? Event matching can stop for those contracts until re-enabled.`,
        confirmText: 'Disable selected',
        cancelText: 'Cancel',
        confirmColor: 'red',
      })

      if (!confirmed) {
        return
      }
    }

    setBulkActionError(null)
    setBulkActionPending(true)

    try {
      await Promise.all(
        selectedIds.map((id) =>
          enabled
            ? disableContractMutation.mutateAsync(id)
            : enableContractMutation.mutateAsync(id),
        ),
      )

      clearSelection()
      notifications.success(
        `${selectedIds.length} contract${selectedIds.length === 1 ? '' : 's'} ${enabled ? 'disabled' : 'enabled'}.`,
        'Bulk update',
      )
    } catch (error) {
      setBulkActionError(getApiErrorMessage(error, 'Unable to apply bulk action.'))
      notifications.error(getApiErrorMessage(error, 'Unable to apply bulk action.'), 'Bulk update')
    } finally {
      setBulkActionPending(false)
    }
  }

  if (!canReadContracts) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Contracts</Heading>
          <Text color="gray">You do not have permission to read contracts.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  return (
    <Flex direction="column" gap="5">
      <div className="page-header">
        <div>
          <h1 className="page-title">Contracts</h1>
          <p className="page-subtitle">
            Define how customer events are matched and delivered to destinations.
          </p>
        </div>
        {canWriteContracts ? (
          <Button asChild size="2">
            <NavLink to="/contracts/new">New contract</NavLink>
          </Button>
        ) : null}
      </div>

      <Card>
        <Flex direction="column" gap="3">
          <Flex gap="3" wrap="wrap" align="end" justify="between">
            <form onSubmit={handleSearchSubmit}>
              <Flex gap="2" align="center">
                <TextField.Root
                  size="2"
                  className="toolbar-search"
                  placeholder="Search id, customer, name, event type"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                  aria-label="Search contracts"
                >
                  <TextField.Slot>
                    <SearchIcon width={15} height={15} />
                  </TextField.Slot>
                </TextField.Root>
                <Button type="submit" size="2" variant="soft">
                  Search
                </Button>
              </Flex>
            </form>

            <Flex gap="2" align="center" wrap="wrap">
              {selectedCount > 0 ? (
                <Flex gap="2" align="center">
                  <Text size="2" color="gray">{selectedCount} selected</Text>
                  <Button
                    size="1"
                    variant="soft"
                    disabled={bulkActionPending || !canWriteContracts}
                    onClick={() => {
                      void handleBulkToggle(false)
                    }}
                  >
                    Enable selected
                  </Button>
                  <Button
                    size="1"
                    variant="soft"
                    disabled={bulkActionPending || !canWriteContracts}
                    onClick={() => {
                      void handleBulkToggle(true)
                    }}
                  >
                    Disable selected
                  </Button>
                  <Button size="1" variant="ghost" onClick={clearSelection}>
                    Clear
                  </Button>
                </Flex>
              ) : null}

              <TextField.Root
                placeholder="Filter customer id"
                value={customerIdParam}
                onChange={(event) =>
                  updateParams({
                    customerId: event.target.value.trim() || null,
                    page: '1',
                  })
                }
              />

              <Select.Root
                value={
                  enabledParam === undefined
                    ? 'all'
                    : enabledParam
                      ? 'enabled'
                      : 'disabled'
                }
                onValueChange={(value) => {
                  updateParams({
                    enabled:
                      value === 'all'
                        ? null
                        : value === 'enabled'
                          ? 'true'
                          : 'false',
                    page: '1',
                  })
                }}
              >
                <Select.Trigger placeholder="Status" />
                <Select.Content>
                  <Select.Item value="all">All statuses</Select.Item>
                  <Select.Item value="enabled">Enabled</Select.Item>
                  <Select.Item value="disabled">Disabled</Select.Item>
                </Select.Content>
              </Select.Root>

              <Select.Root
                value={destinationTypeParam ?? 'all'}
                onValueChange={(value) =>
                  updateParams({
                    destinationType: value === 'all' ? null : value,
                    page: '1',
                  })
                }
              >
                <Select.Trigger placeholder="Destination" />
                <Select.Content>
                  <Select.Item value="all">All destination types</Select.Item>
                  {destinationValues.map((value) => (
                    <Select.Item key={value} value={value}>
                      {value}
                    </Select.Item>
                  ))}
                </Select.Content>
              </Select.Root>

              <Select.Root
                value={String(pageSizeParam)}
                onValueChange={(value) => updateParams({ pageSize: value, page: '1' })}
              >
                <Select.Trigger />
                <Select.Content>
                  <Select.Item value="10">10</Select.Item>
                  <Select.Item value="20">20</Select.Item>
                  <Select.Item value="50">50</Select.Item>
                  <Select.Item value="100">100</Select.Item>
                </Select.Content>
              </Select.Root>

              <Button
                type="button"
                variant="soft"
                onClick={() =>
                  setSearchParams(
                    new URLSearchParams({
                      page: '1',
                      pageSize: String(defaultPageSize),
                    }),
                  )
                }
              >
                Reset filters
              </Button>
            </Flex>
          </Flex>

          <ListFeedback
            isLoading={contractsQuery.isLoading}
            isError={contractsQuery.isError}
            error={contractsQuery.error}
            isEmpty={contracts.length === 0}
            loadingMessage="Loading contracts..."
            errorMessage={
              contractsQuery.error instanceof ApiError && contractsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(contractsQuery.error, 'Unable to load contracts.')
            }
            emptyMessage="No contracts found for the selected filters."
            emptyTitle={hasActiveFilters ? 'No contracts match current filters' : 'No contracts yet'}
            emptyDescription={
              hasActiveFilters
                ? 'Try adjusting or clearing filters to see matching contracts.'
                : canWriteContracts
                  ? 'Create your first contract to start routing events to destinations.'
                  : 'Contracts will appear here when available.'
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
                      void contractsQuery.refetch()
                    },
                  }
            }
          >
            <>
              {bulkActionError ? (
                <Text size="2" color="red">
                  {bulkActionError}
                </Text>
              ) : null}

              <Table.Root size="2">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>
                      <input
                        type="checkbox"
                        checked={allVisibleSelected}
                        onChange={toggleVisibleSelection}
                        aria-label="Select all visible contracts"
                      />
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('name')}>
                        Name
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('customerId')}>
                        Customer
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Event types</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('destinationType')}>
                        Destination
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('updatedAt')}>
                        Updated
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {contracts.map((contract) => {
                    const pending = rowActionId === contract.id

                    return (
                      <Table.Row key={contract.id}>
                        <Table.Cell>
                          <input
                            type="checkbox"
                            checked={isSelected(contract.id)}
                            onChange={() => toggleSelection(contract.id)}
                            aria-label={`Select contract ${contract.id}`}
                          />
                        </Table.Cell>
                        <Table.RowHeaderCell>{contract.name ?? contract.id}</Table.RowHeaderCell>
                        <Table.Cell>{contract.customerId}</Table.Cell>
                        <Table.Cell>{contract.eventTypes.join(', ') || '-'}</Table.Cell>
                        <Table.Cell>{contract.destinationType}</Table.Cell>
                        <Table.Cell>
                          <Badge color={contract.enabled ? 'green' : 'gray'}>
                            {contract.enabled ? 'Enabled' : 'Disabled'}
                          </Badge>
                        </Table.Cell>
                        <Table.Cell>{new Date(contract.updatedAt).toLocaleString()}</Table.Cell>
                        <Table.Cell>
                          <Flex gap="2">
                            <Link asChild size="1">
                              <NavLink to={`/contracts/${encodeURIComponent(contract.id)}`}>
                                Detail
                              </NavLink>
                            </Link>
                            <Link asChild size="1">
                              <NavLink to={`/test-events?contractId=${encodeURIComponent(contract.id)}`}>
                                Test event
                              </NavLink>
                            </Link>
                            <Button
                              size="1"
                              variant="soft"
                              disabled={!canWriteContracts || pending}
                              onClick={() =>
                                handleToggleEnabled(contract.id, contract.enabled)
                              }
                            >
                              {pending
                                ? 'Saving...'
                                : contract.enabled
                                  ? 'Disable'
                                  : 'Enable'}
                            </Button>
                          </Flex>
                        </Table.Cell>
                      </Table.Row>
                    )
                  })}
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
