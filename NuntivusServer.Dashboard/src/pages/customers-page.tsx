import {
  Badge,
  Button,
  Card,
  Dialog,
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
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { FormDialog } from '../components/dialogs/form-dialog'
import { getServerValidation } from '../components/forms/form-validation'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { PowerIcon } from '../components/icons/nav-icons'
import { DetailAction, NavigableTableRow, TableIconButton } from '../components/tables/table-actions'
import {
  useCreateCustomerMutation,
  useCustomersQuery,
  useDisableCustomerMutation,
  useEnableCustomerMutation,
} from '../features/customers/use-customers'
import { useBulkSelection } from '../hooks/use-bulk-selection'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import {
  parseBooleanParam,
  parsePositiveInt,
  useUrlQueryParams,
} from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { CustomerListQuery } from '../services/api/customers-api'
import { useAppSelector } from '../store/hooks'

type SortBy = 'id' | 'name' | 'createdAt' | 'updatedAt'
type SortDirection = 'asc' | 'desc'

export function CustomersPage() {
  const { searchParams, updateParams } = useUrlQueryParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
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
  const [customerIdTouched, setCustomerIdTouched] = useState(false)
  const [customerNameTouched, setCustomerNameTouched] = useState(false)
  const [isCreateCustomerOpen, setIsCreateCustomerOpen] = useState(false)
  const createCustomerButtonRef = useRef<HTMLButtonElement>(null)
  const [rowActionId, setRowActionId] = useState<string | null>(null)
  const [bulkActionPending, setBulkActionPending] = useState(false)
  const [bulkActionError, setBulkActionError] = useState<string | null>(null)
  const [createError, setCreateError] = useState<string | null>(null)

  const customerIdError = !newCustomerId.trim() ? 'Customer id is required.' : null
  const customerNameError = !newCustomerName.trim() ? 'Customer name is required.' : null

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

    if (!canWriteCustomers || customerIdError || customerNameError) {
      setCustomerIdTouched(true)
      setCustomerNameTouched(true)
      return
    }

    setCreateError(null)

    try {
      await createCustomerMutation.mutateAsync({
        id: newCustomerId.trim(),
        name: newCustomerName.trim(),
      })

      setNewCustomerId('')
      setNewCustomerName('')
      setCustomerIdTouched(false)
      setCustomerNameTouched(false)
      setIsCreateCustomerOpen(false)
      notifications.success('Customer created successfully.', 'Create customer')
    } catch (error) {
      setCreateError(getServerValidation(error, 'Unable to create customer.').formError)
      notifications.error(getApiErrorMessage(error, 'Unable to create customer.'), 'Create customer')
    }
  }

  function handleCreateCustomerOpenChange(open: boolean) {
    setIsCreateCustomerOpen(open)

    if (!open) {
      setCreateError(null)
      setCustomerIdTouched(false)
      setCustomerNameTouched(false)
      window.setTimeout(() => createCustomerButtonRef.current?.focus(), 0)
    }
  }

  async function handleToggleEnabled(id: string, enabled: boolean) {
    if (!canWriteCustomers) {
      return
    }

    if (enabled) {
      const confirmed = await confirm({
        title: 'Disable customer',
        description:
          'Disable this customer? New deliveries for this customer may be blocked until it is enabled again.',
        confirmText: 'Disable customer',
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
        await disableCustomerMutation.mutateAsync(id)
        notifications.success(`Customer ${id} disabled.`, 'Customer updated')
      } else {
        await enableCustomerMutation.mutateAsync(id)
        notifications.success(`Customer ${id} enabled.`, 'Customer updated')
      }
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to update customer.'), 'Customer updated')
    } finally {
      setRowActionId(null)
    }
  }

  const customers = useMemo(() => customersQuery.data?.items ?? [], [customersQuery.data?.items])
  const paging = customersQuery.data
  const hasActiveFilters = searchParam.trim().length > 0 || enabledParam !== undefined
  const visibleIds = useMemo(() => customers.map((customer) => customer.id), [customers])
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
    if (!canWriteCustomers || selectedIds.length === 0) {
      return
    }

    if (enabled) {
      const confirmed = await confirm({
        title: 'Disable selected customers',
        description: `Disable ${selectedIds.length} selected customer${selectedIds.length === 1 ? '' : 's'}? New deliveries for those customers may be blocked until re-enabled.`,
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
            ? disableCustomerMutation.mutateAsync(id)
            : enableCustomerMutation.mutateAsync(id),
        ),
      )

      clearSelection()
      notifications.success(
        `${selectedIds.length} customer${selectedIds.length === 1 ? '' : 's'} ${enabled ? 'disabled' : 'enabled'}.`,
        'Bulk update',
      )
    } catch (error) {
      setBulkActionError(getApiErrorMessage(error, 'Unable to apply bulk action.'))
      notifications.error(getApiErrorMessage(error, 'Unable to apply bulk action.'), 'Bulk update')
    } finally {
      setBulkActionPending(false)
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

  return (
    <Flex direction="column" gap="5">
      <div className="page-header">
        <div>
          <h1 className="page-title">Customers</h1>
          <p className="page-subtitle">
            Manage the customers that own contracts and receive deliveries.
          </p>
        </div>
        {canWriteCustomers ? (
          <Button
            ref={createCustomerButtonRef}
            size="2"
            onClick={() => setIsCreateCustomerOpen(true)}
          >
            New customer
          </Button>
        ) : null}
      </div>

      {canWriteCustomers ? (
        <FormDialog
          open={isCreateCustomerOpen}
          onOpenChange={handleCreateCustomerOpenChange}
          title="Create customer"
          description="Add a customer that can own contracts and receive deliveries."
          descriptionId="customers-create-dialog-description"
          footer={
            <>
              <Dialog.Close>
                <Button type="button" variant="soft" color="gray">
                  Cancel
                </Button>
              </Dialog.Close>
              <Button
                type="submit"
                form="customers-create-form"
                disabled={
                  createCustomerMutation.isPending ||
                  !!customerIdError ||
                  !!customerNameError
                }
              >
                {createCustomerMutation.isPending ? 'Creating…' : 'Create customer'}
              </Button>
            </>
          }
        >
          <form id="customers-create-form" onSubmit={handleCreateCustomer}>
            <div className="field">
              <label className="field-label" htmlFor="customers-create-id">
                Customer id
              </label>
              <TextField.Root
                id="customers-create-id"
                size="2"
                placeholder="e.g. acme"
                value={newCustomerId}
                onChange={(event) => setNewCustomerId(event.target.value)}
                aria-invalid={customerIdTouched && customerIdError ? 'true' : 'false'}
                aria-describedby="customers-create-id-error"
                onBlur={() => setCustomerIdTouched(true)}
              />
              {customerIdTouched ? (
                <FieldErrorText id="customers-create-id-error" message={customerIdError} />
              ) : null}
            </div>

            <div className="field">
              <label className="field-label" htmlFor="customers-create-name">
                Customer name
              </label>
              <TextField.Root
                id="customers-create-name"
                size="2"
                placeholder="Acme Inc."
                value={newCustomerName}
                onChange={(event) => setNewCustomerName(event.target.value)}
                aria-invalid={customerNameTouched && customerNameError ? 'true' : 'false'}
                aria-describedby="customers-create-name-error"
                onBlur={() => setCustomerNameTouched(true)}
              />
              {customerNameTouched ? (
                <FieldErrorText id="customers-create-name-error" message={customerNameError} />
              ) : null}
            </div>

            <FormErrorText
              id="customers-create-submit-error"
              message={
                createError ??
                (createCustomerMutation.isError
                  ? getApiErrorMessage(createCustomerMutation.error, 'Unable to create customer.')
                  : null)
              }
            />
          </form>
        </FormDialog>
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
              {selectedCount > 0 ? (
                <Flex gap="2" align="center">
                  <Text size="2" color="gray">{selectedCount} selected</Text>
                  <Button
                    size="1"
                    variant="soft"
                    disabled={bulkActionPending || !canWriteCustomers}
                    onClick={() => {
                      void handleBulkToggle(false)
                    }}
                  >
                    Enable selected
                  </Button>
                  <Button
                    size="1"
                    variant="soft"
                    disabled={bulkActionPending || !canWriteCustomers}
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

          <ListFeedback
            isLoading={customersQuery.isLoading}
            isError={customersQuery.isError}
            error={customersQuery.error}
            isEmpty={customers.length === 0}
            loadingMessage="Loading customers..."
            errorMessage={
              customersQuery.error instanceof ApiError && customersQuery.error.status === 403
                ? 'The backend denied access (403).'
                : getApiErrorMessage(customersQuery.error, 'Unable to load customers.')
            }
            emptyMessage="No customers found for the selected filters."
            emptyTitle={hasActiveFilters ? 'No customers match current filters' : 'No customers yet'}
            emptyDescription={
              hasActiveFilters
                ? 'Try broadening or clearing filters to see matching customers.'
                : canWriteCustomers
                  ? 'Create your first customer to start managing contracts and deliveries.'
                  : 'Customers will appear here when available.'
            }
            emptyAction={
              hasActiveFilters
                ? {
                  label: 'Clear filters',
                  onClick: () =>
                    updateParams({
                      search: null,
                      enabled: null,
                      sortBy: null,
                      sortDirection: null,
                      page: '1',
                      pageSize: null,
                    }),
                }
                : {
                  label: 'Refresh',
                  onClick: () => {
                    void customersQuery.refetch()
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

              <div className="responsive-table">
                <Table.Root aria-label="Customers table">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>
                        <input
                          type="checkbox"
                          checked={allVisibleSelected}
                          onChange={toggleVisibleSelection}
                          aria-label="Select all visible customers"
                        />
                      </Table.ColumnHeaderCell>
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
                        <NavigableTableRow
                          key={customer.id}
                          to={`/customers/${encodeURIComponent(customer.id)}`}
                          label={`customer ${customer.name}`}
                        >
                          <Table.Cell>
                            <input
                              type="checkbox"
                              checked={isSelected(customer.id)}
                              onChange={() => toggleSelection(customer.id)}
                              aria-label={`Select customer ${customer.id}`}
                            />
                          </Table.Cell>
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
                            <div className="table-row-actions">
                              <TableIconButton
                                label={pending ? 'Saving customer' : customer.enabled ? 'Disable customer' : 'Enable customer'}
                                disabled={!canWriteCustomers || pending}
                                onClick={() =>
                                  handleToggleEnabled(customer.id, customer.enabled)
                                }
                                color={customer.enabled ? 'amber' : 'green'}
                              >
                                <PowerIcon width={15} height={15} />
                              </TableIconButton>
                              <DetailAction to={`/customers/${encodeURIComponent(customer.id)}`} label={`Open ${customer.name}`} />
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
