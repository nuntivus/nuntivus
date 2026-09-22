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
  useCreateCustomerMutation,
  useCustomersQuery,
  useDisableCustomerMutation,
  useEnableCustomerMutation,
} from '../features/customers/use-customers'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { CustomerListQuery } from '../services/api/customers-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'id' | 'name' | 'createdAt' | 'updatedAt'
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

export function CustomersPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadCustomers = hasPermission(currentUser, 'customers.read')
  const canWriteCustomers = hasPermission(currentUser, 'customers.write')

  const searchParam = searchParams.get('search') ?? ''
  const enabledParam = parseBooleanParam(searchParams.get('enabled'))
  const sortByParam =
    (searchParams.get('sortBy') as SortBy | null) ?? ('createdAt' as SortBy)
  const sortDirectionParam =
    (searchParams.get('sortDirection') as SortDirection | null) ??
    ('desc' as SortDirection)
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), 20)

  const query = useMemo<CustomerListQuery>(
    () => ({
      search: searchParam,
      enabled: enabledParam,
      sortBy: sortByParam,
      sortDirection: sortDirectionParam,
      page: pageParam,
      pageSize: pageSizeParam,
    }),
    [enabledParam, pageParam, pageSizeParam, searchParam, sortByParam, sortDirectionParam],
  )

  const customersQuery = useCustomersQuery(query)
  const createCustomerMutation = useCreateCustomerMutation()
  const enableCustomerMutation = useEnableCustomerMutation()
  const disableCustomerMutation = useDisableCustomerMutation()

  const [searchInput, setSearchInput] = useState(searchParam)
  const [newCustomerId, setNewCustomerId] = useState('')
  const [newCustomerName, setNewCustomerName] = useState('')
  const [rowActionId, setRowActionId] = useState<string | null>(null)

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

  async function handleCreateCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteCustomers || !newCustomerId.trim() || !newCustomerName.trim()) {
      return
    }

    await createCustomerMutation.mutateAsync({
      id: newCustomerId.trim(),
      name: newCustomerName.trim(),
    })

    setNewCustomerId('')
    setNewCustomerName('')
  }

  async function handleToggleEnabled(id: string, enabled: boolean) {
    if (!canWriteCustomers) {
      return
    }

    setRowActionId(id)

    try {
      if (enabled) {
        await disableCustomerMutation.mutateAsync(id)
      } else {
        await enableCustomerMutation.mutateAsync(id)
      }
    } finally {
      setRowActionId(null)
    }
  }

  if (!canReadCustomers) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Customers</Heading>
          <Text color="gray">You do not have permission to read customers.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const customers = customersQuery.data?.items ?? []
  const paging = customersQuery.data

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Customers</Heading>

      {canWriteCustomers ? (
        <Card>
          <form onSubmit={handleCreateCustomer}>
            <Flex direction="column" gap="3">
              <Heading size="4">Create customer</Heading>
              <TextField.Root
                placeholder="Customer id (e.g. acme)"
                value={newCustomerId}
                onChange={(event) => setNewCustomerId(event.target.value)}
              />
              <TextField.Root
                placeholder="Customer name"
                value={newCustomerName}
                onChange={(event) => setNewCustomerName(event.target.value)}
              />
              {createCustomerMutation.isError ? (
                <Text color="red" size="2">
                  {getApiErrorMessage(createCustomerMutation.error, 'Unable to create customer.')}
                </Text>
              ) : null}
              <Button
                type="submit"
                disabled={
                  createCustomerMutation.isPending ||
                  !newCustomerId.trim() ||
                  !newCustomerName.trim()
                }
              >
                {createCustomerMutation.isPending ? 'Creating...' : 'Create customer'}
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
                  placeholder="Search id or name"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                />
                <Button type="submit">Search</Button>
              </Flex>
            </form>

            <Flex gap="2" align="center">
              <Text size="2" color="gray">
                Enabled
              </Text>
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
                <Select.Trigger />
                <Select.Content>
                  <Select.Item value="all">All</Select.Item>
                  <Select.Item value="enabled">Enabled</Select.Item>
                  <Select.Item value="disabled">Disabled</Select.Item>
                </Select.Content>
              </Select.Root>

              <Text size="2" color="gray">
                Page size
              </Text>
              <Select.Root
                value={String(pageSizeParam)}
                onValueChange={(value) =>
                  updateParams({ pageSize: value, page: '1' })
                }
              >
                <Select.Trigger />
                <Select.Content>
                  <Select.Item value="10">10</Select.Item>
                  <Select.Item value="20">20</Select.Item>
                  <Select.Item value="50">50</Select.Item>
                </Select.Content>
              </Select.Root>
            </Flex>
          </Flex>

          {customersQuery.isLoading ? (
            <Text size="2" color="gray">
              Loading customers...
            </Text>
          ) : customersQuery.isError ? (
            <Text size="2" color="red">
              {customersQuery.error instanceof ApiError &&
              customersQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(customersQuery.error, 'Unable to load customers.')}
            </Text>
          ) : customers.length === 0 ? (
            <Card>
              <Text size="2" color="gray">
                No customers found for the selected filters.
              </Text>
            </Card>
          ) : (
            <>
              <Table.Root>
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('id')}>
                        ID
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('name')}>
                        Name
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>State</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <Button variant="ghost" onClick={() => handleSort('updatedAt')}>
                        Updated
                      </Button>
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {customers.map((customer) => {
                    const pending = rowActionId === customer.id

                    return (
                      <Table.Row key={customer.id}>
                        <Table.RowHeaderCell>{customer.id}</Table.RowHeaderCell>
                        <Table.Cell>{customer.name}</Table.Cell>
                        <Table.Cell>
                          <Badge color={customer.enabled ? 'green' : 'gray'}>
                            {customer.enabled ? 'Enabled' : 'Disabled'}
                          </Badge>
                        </Table.Cell>
                        <Table.Cell>
                          {new Date(customer.updatedAt).toLocaleString()}
                        </Table.Cell>
                        <Table.Cell>
                          <Flex gap="2">
                            <Button asChild size="1" variant="ghost">
                              <NavLink to={`/customers/${encodeURIComponent(customer.id)}`}>
                                Detail
                              </NavLink>
                            </Button>
                            <Button
                              size="1"
                              variant="soft"
                              disabled={!canWriteCustomers || pending}
                              onClick={() =>
                                handleToggleEnabled(customer.id, customer.enabled)
                              }
                            >
                              {pending
                                ? 'Saving...'
                                : customer.enabled
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
