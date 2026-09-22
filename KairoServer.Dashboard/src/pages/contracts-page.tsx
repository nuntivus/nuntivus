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
import {
  useContractsQuery,
  useCreateContractMutation,
  useDisableContractMutation,
  useEnableContractMutation,
} from '../features/contracts/use-contracts'
import { useLocalUserPreferences } from '../features/settings/local-user-preferences'
import {
  deleteSavedView,
  getSavedViews,
  upsertSavedView,
  type SavedView,
} from '../features/saved-views/saved-views-storage'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { ContractListQuery, DestinationType } from '../services/api/contracts-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'createdAt' | 'updatedAt' | 'name' | 'customerId' | 'destinationType'
type SortDirection = 'asc' | 'desc'

function parseBooleanParam(value: string | null): boolean | undefined {
  if (value === 'true') {
    return true
  }

  if (value === 'false') {
    return false
  }

  return undefined
}

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

const destinationValues: DestinationType[] = [
  'Http',
  'Kafka',
  'Nats',
  'RabbitMq',
  'Sqs',
]

export function ContractsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector((state) => state.auth)
  const { preferences } = useLocalUserPreferences(currentUser?.subject)
  const [savedViews, setSavedViews] = useState<SavedView[]>([])
  const [selectedSavedViewId, setSelectedSavedViewId] = useState('')
  const [savedViewName, setSavedViewName] = useState('')

  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')
  const defaultPageSize = preferences.tablePageSizes.contracts || preferences.defaultPageSize

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
  const createContractMutation = useCreateContractMutation()
  const enableContractMutation = useEnableContractMutation()
  const disableContractMutation = useDisableContractMutation()

  const [searchInput, setSearchInput] = useState(searchParam)
  const [createCustomerId, setCreateCustomerId] = useState('')
  const [createName, setCreateName] = useState('')
  const [createEventType, setCreateEventType] = useState('')
  const [createUrl, setCreateUrl] = useState('')
  const [rowActionId, setRowActionId] = useState<string | null>(null)

  useEffect(() => {
    setSavedViews(getSavedViews(currentUser?.subject, 'contracts'))
  }, [currentUser?.subject])

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

    const next = upsertSavedView(currentUser.subject, 'contracts', {
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

    const next = deleteSavedView(currentUser.subject, 'contracts', selectedSavedViewId)
    setSavedViews(next)
    setSelectedSavedViewId('')
  }

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

  async function handleCreateContract(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (
      !canWriteContracts ||
      !createCustomerId.trim() ||
      !createEventType.trim() ||
      !createUrl.trim()
    ) {
      return
    }

    await createContractMutation.mutateAsync({
      customerId: createCustomerId.trim(),
      name: createName.trim() || null,
      eventTypes: [createEventType.trim()],
      url: createUrl.trim(),
      authenticationType: 'none',
    })

    setCreateCustomerId('')
    setCreateName('')
    setCreateEventType('')
    setCreateUrl('')
  }

  async function handleToggleEnabled(id: string, enabled: boolean) {
    if (!canWriteContracts) {
      return
    }

    setRowActionId(id)

    try {
      if (enabled) {
        await disableContractMutation.mutateAsync(id)
      } else {
        await enableContractMutation.mutateAsync(id)
      }
    } finally {
      setRowActionId(null)
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

  const contracts = contractsQuery.data?.items ?? []
  const paging = contractsQuery.data

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Contracts</Heading>

      {canWriteContracts ? (
        <Flex>
          <Button asChild>
            <NavLink to="/contracts/new">New contract</NavLink>
          </Button>
        </Flex>
      ) : null}

      {canWriteContracts ? (
        <Card>
          <form onSubmit={handleCreateContract}>
            <Flex direction="column" gap="3">
              <Heading size="4">Create contract</Heading>
              <TextField.Root
                placeholder="Customer id"
                value={createCustomerId}
                onChange={(event) => setCreateCustomerId(event.target.value)}
              />
              <TextField.Root
                placeholder="Contract name (optional)"
                value={createName}
                onChange={(event) => setCreateName(event.target.value)}
              />
              <TextField.Root
                placeholder="Event type (e.g. orders.created)"
                value={createEventType}
                onChange={(event) => setCreateEventType(event.target.value)}
              />
              <TextField.Root
                placeholder="HTTP destination URL (https://...)"
                value={createUrl}
                onChange={(event) => setCreateUrl(event.target.value)}
              />

              {createContractMutation.isError ? (
                <Text color="red" size="2">
                  {getApiErrorMessage(createContractMutation.error, 'Unable to create contract.')}
                </Text>
              ) : null}

              <Button
                type="submit"
                disabled={
                  createContractMutation.isPending ||
                  !createCustomerId.trim() ||
                  !createEventType.trim() ||
                  !createUrl.trim()
                }
              >
                {createContractMutation.isPending ? 'Creating...' : 'Create contract'}
              </Button>
            </Flex>
          </form>
        </Card>
      ) : null}

      <Card>
        <Flex direction="column" gap="3">
          <Flex gap="3" wrap="wrap" align="end" justify="between">
            <form onSubmit={handleSearchSubmit}>
              <Flex gap="2" align="center">
                <TextField.Root
                  placeholder="Search id, customer, name, event type"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                />
                <Button type="submit">Search</Button>
              </Flex>
            </form>

            <Flex gap="2" align="center" wrap="wrap">
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

          {contractsQuery.isLoading ? (
            <Text size="2" color="gray">
              Loading contracts...
            </Text>
          ) : contractsQuery.isError ? (
            <Text size="2" color="red">
              {contractsQuery.error instanceof ApiError &&
              contractsQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(contractsQuery.error, 'Unable to load contracts.')}
            </Text>
          ) : contracts.length === 0 ? (
            <Card>
              <Text size="2" color="gray">
                No contracts found for the selected filters.
              </Text>
            </Card>
          ) : (
            <>
              <Table.Root size={preferences.denseTables ? '1' : '2'}>
                <Table.Header>
                  <Table.Row>
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
                            <Button asChild size="1" variant="ghost">
                              <NavLink to={`/contracts/${encodeURIComponent(contract.id)}`}>
                                Detail
                              </NavLink>
                            </Button>
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
                    onClick={() =>
                      updateParams({ page: String(Math.max(1, pageParam - 1)) })
                    }
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
