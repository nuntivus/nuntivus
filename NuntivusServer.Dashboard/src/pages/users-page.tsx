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
import { ListFeedback } from '../components/lists/list-feedback'
import { TrashIcon } from '../components/icons/nav-icons'
import { DetailAction, NavigableTableRow, TableIconButton } from '../components/tables/table-actions'
import {
  useAdminRolesQuery,
  useAdminUsersQuery,
  useCreateAdminUserMutation,
  useDeleteAdminUserMutation,
} from '../features/admin-users/use-admin-users'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useBulkSelection } from '../hooks/use-bulk-selection'
import { useNotifications } from '../hooks/use-notifications'
import { useUrlQueryParams } from '../hooks/use-url-query-params'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

export function UsersPage() {
  const { searchParams, updateParams } = useUrlQueryParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)
  const canReadUsers = hasPermission(currentUser, 'users.read')
  const canManageRoles = hasPermission(currentUser, 'roles.manage')

  const usersQuery = useAdminUsersQuery()
  const rolesQuery = useAdminRolesQuery()
  const createUserMutation = useCreateAdminUserMutation()
  const deleteUserMutation = useDeleteAdminUserMutation()

  const [subject, setSubject] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [selectedRoles, setSelectedRoles] = useState<string[]>(['viewer'])
  const [isCreateUserOpen, setIsCreateUserOpen] = useState(false)
  const createUserButtonRef = useRef<HTMLButtonElement>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bulkPending, setBulkPending] = useState(false)
  const [rowActionSubject, setRowActionSubject] = useState<string | null>(null)

  const searchTerm = searchParams.get('search') ?? ''
  const selectedRoleFilter = searchParams.get('role') ?? 'all'
  const searchInput = searchTerm

  const users = useMemo(() => usersQuery.data ?? [], [usersQuery.data])
  const filteredUsers = useMemo(() => {
    const normalizedSearch = searchTerm.trim().toLowerCase()

    return users.filter((user) => {
      const searchMatches =
        !normalizedSearch ||
        user.subject.toLowerCase().includes(normalizedSearch) ||
        (user.displayName ?? '').toLowerCase().includes(normalizedSearch)

      const roleMatches =
        selectedRoleFilter === 'all' || user.roles.includes(selectedRoleFilter)

      return searchMatches && roleMatches
    })
  }, [searchTerm, selectedRoleFilter, users])

  const visibleIds = useMemo(
    () => filteredUsers.map((user) => user.subject),
    [filteredUsers],
  )
  const {
    selectedIds,
    selectedCount,
    allVisibleSelected,
    toggleSelection,
    toggleVisibleSelection,
    clearSelection,
    isSelected,
  } = useBulkSelection(visibleIds)

  const availableRoleOptions = useMemo(() => {
    const set = new Set<string>()

    users.forEach((user) => {
      user.roles.forEach((role) => set.add(role))
    })

    return Array.from(set).sort()
  }, [users])

  const createError = useMemo(
    () => getApiErrorMessage(createUserMutation.error, 'Unable to create user'),
    [createUserMutation.error],
  )
  const subjectError = !subject.trim() ? 'Subject is required.' : null
  const passwordError = password.trim().length > 0 && password.trim().length < 12
    ? 'Password must be at least 12 characters.'
    : null
  const roleSelectionError = selectedRoles.length === 0 ? 'At least one role is required.' : null

  async function handleDelete(subjectToDelete: string) {
    if (!canManageRoles) {
      return
    }

    const confirmed = await confirm({
      title: 'Delete user',
      description: `Delete user "${subjectToDelete}"? This permanently removes assigned roles and access until re-created.`,
      confirmText: 'Delete',
      cancelText: 'Cancel',
      confirmColor: 'red',
    })

    if (!confirmed) {
      return
    }

    setActionError(null)
    setRowActionSubject(subjectToDelete)

    try {
      await deleteUserMutation.mutateAsync(subjectToDelete)
      clearSelection()
      notifications.success(`User ${subjectToDelete} deleted.`, 'Delete user')
    } catch (error) {
      setActionError(getApiErrorMessage(error, 'Unable to delete user.'))
      notifications.error(getApiErrorMessage(error, 'Unable to delete user.'), 'Delete user')
    } finally {
      setRowActionSubject(null)
    }
  }

  async function handleBulkDelete() {
    if (!canManageRoles || selectedIds.length === 0) {
      return
    }

    const confirmed = await confirm({
      title: 'Delete selected users',
      description: `Delete ${selectedIds.length} selected users? This permanently removes assigned roles and access until re-created.`,
      confirmText: 'Delete',
      cancelText: 'Cancel',
      confirmColor: 'red',
    })

    if (!confirmed) {
      return
    }

    setActionError(null)
    setBulkPending(true)

    try {
      await Promise.all(selectedIds.map((item) => deleteUserMutation.mutateAsync(item)))
      clearSelection()
      notifications.success(
        `${selectedIds.length} user${selectedIds.length === 1 ? '' : 's'} deleted.`,
        'Delete users',
      )
    } catch (error) {
      setActionError(getApiErrorMessage(error, 'Unable to delete selected users.'))
      notifications.error(getApiErrorMessage(error, 'Unable to delete selected users.'), 'Delete users')
    } finally {
      setBulkPending(false)
    }
  }

  function toggleRole(roleName: string) {
    setSelectedRoles((current) => {
      if (current.includes(roleName)) {
        return current.filter((value) => value !== roleName)
      }

      return [...current, roleName]
    })
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canManageRoles || !!subjectError || !!passwordError || !!roleSelectionError) {
      return
    }

    try {
      await createUserMutation.mutateAsync({
        subject: subject.trim(),
        displayName: displayName.trim() || null,
        roles: selectedRoles,
        password: password.trim() || null,
      })

      setSubject('')
      setDisplayName('')
      setPassword('')
      setSelectedRoles(['viewer'])
      setIsCreateUserOpen(false)
      notifications.success('User created successfully.', 'Create user')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to create user.'), 'Create user')
    }
  }

  function handleCreateUserOpenChange(open: boolean) {
    setIsCreateUserOpen(open)

    if (!open) {
      window.setTimeout(() => createUserButtonRef.current?.focus(), 0)
    }
  }

  if (!canReadUsers) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Users</Heading>
          <Text color="gray">You do not have permission to read users.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const usersError = usersQuery.error
  const forbiddenFromApi =
    usersError instanceof ApiError && usersError.status === 403

  return (
    <Flex direction="column" gap="5">
      <div className="page-header">
        <div>
          <h1 className="page-title">Users &amp; RBAC</h1>
          <p className="page-subtitle">
            Manage administrator accounts and their role assignments.
          </p>
        </div>
        {canManageRoles ? (
          <Button
            ref={createUserButtonRef}
            size="2"
            onClick={() => setIsCreateUserOpen(true)}
          >
            New user
          </Button>
        ) : null}
      </div>

      {canManageRoles ? (
        <FormDialog
          open={isCreateUserOpen}
          onOpenChange={handleCreateUserOpenChange}
          title="Invite or create user"
          description="Create an administrator account and assign one or more roles."
          descriptionId="users-create-dialog-description"
          footer={
            <>
              <Dialog.Close>
                <Button type="button" variant="soft" color="gray">
                  Cancel
                </Button>
              </Dialog.Close>
              <Button
                type="submit"
                form="users-create-form"
                disabled={
                  createUserMutation.isPending ||
                  !!subjectError ||
                  !!passwordError ||
                  !!roleSelectionError
                }
              >
                {createUserMutation.isPending ? 'Creating…' : 'Create user'}
              </Button>
            </>
          }
        >
          <form id="users-create-form" onSubmit={handleCreate}>
            <div className="field">
              <label className="field-label" htmlFor="users-create-subject">
                Subject
              </label>
              <TextField.Root
                id="users-create-subject"
                size="2"
                placeholder="user id or email"
                value={subject}
                onChange={(event) => setSubject(event.target.value)}
                aria-invalid={subjectError ? 'true' : 'false'}
                aria-describedby={subjectError ? 'users-create-subject-error' : undefined}
              />
              <FieldErrorText id="users-create-subject-error" message={subjectError} />
            </div>

            <div className="field">
              <label className="field-label" htmlFor="users-create-display">
                Display name <span className="field-optional">(optional)</span>
              </label>
              <TextField.Root
                id="users-create-display"
                size="2"
                placeholder="Jane Doe"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
              />
            </div>

            <div className="field">
              <label className="field-label" htmlFor="users-create-password">
                Password <span className="field-optional">(optional, min 12 characters)</span>
              </label>
              <TextField.Root
                id="users-create-password"
                size="2"
                type="password"
                placeholder="••••••••••••"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                aria-invalid={passwordError ? 'true' : 'false'}
                aria-describedby={passwordError ? 'users-create-password-error' : undefined}
              />
              <FieldErrorText id="users-create-password-error" message={passwordError} />
            </div>

            <div className="field">
              <span className="field-label">Roles</span>
              <ListFeedback
                isLoading={rolesQuery.isLoading}
                isError={rolesQuery.isError}
                error={rolesQuery.error}
                isEmpty={(rolesQuery.data ?? []).length === 0}
                loadingMessage="Loading roles..."
                errorMessage={getApiErrorMessage(rolesQuery.error, 'Unable to load roles.')}
                emptyMessage="No roles found."
                skeletonRows={4}
              >
                <Flex direction="column" gap="2" mt="1">
                  {(rolesQuery.data ?? []).map((role) => {
                    const checked = selectedRoles.includes(role.name)

                    return (
                      <label key={role.name}>
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleRole(role.name)}
                        />{' '}
                        {role.name}
                      </label>
                    )
                  })}
                </Flex>
              </ListFeedback>
              <FieldErrorText id="users-create-roles-error" message={roleSelectionError} />
            </div>

            <FormErrorText
              id="users-create-submit-error"
              message={createUserMutation.isError ? createError : null}
            />
          </form>
        </FormDialog>
      ) : (
        <Card>
          <Text size="2" color="gray">
            Role management is admin-only. You can still view user assignments.
          </Text>
        </Card>
      )}

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">User list</Heading>

          <Flex gap="2" wrap="wrap" align="center">
            <TextField.Root
              placeholder="Search subject or name"
              value={searchInput}
              onChange={(event) =>
                updateParams({ search: event.target.value.trim() || null })
              }
            />
            <Select.Root
              value={selectedRoleFilter}
              onValueChange={(value) => updateParams({ role: value === 'all' ? null : value })}
            >
              <Select.Trigger placeholder="Filter by role" />
              <Select.Content>
                <Select.Item value="all">All roles</Select.Item>
                {availableRoleOptions.map((role) => (
                  <Select.Item key={role} value={role}>
                    {role}
                  </Select.Item>
                ))}
              </Select.Content>
            </Select.Root>
            {selectedCount > 0 ? (
              <>
                <Text size="2" color="gray">{selectedCount} selected</Text>
                <Button
                  color="red"
                  variant="soft"
                  size="1"
                  disabled={!canManageRoles || bulkPending}
                  onClick={() => {
                    void handleBulkDelete()
                  }}
                >
                  Delete selected
                </Button>
                <Button size="1" variant="ghost" onClick={clearSelection}>
                  Clear
                </Button>
              </>
            ) : null}
          </Flex>

          <FormErrorText id="users-actions-error" message={actionError} />

          <ListFeedback
            isLoading={usersQuery.isLoading}
            isError={usersQuery.isError}
            error={usersQuery.error}
            isEmpty={filteredUsers.length === 0}
            loadingMessage="Loading users..."
            errorMessage={
              forbiddenFromApi
                ? 'The backend denied access (403).'
                : getApiErrorMessage(usersQuery.error, 'Unable to load users.')
            }
            emptyMessage="No users found for the selected filters."
          >
            <div className="responsive-table">
              <Table.Root aria-label="Users table">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>
                      <input
                        type="checkbox"
                        checked={allVisibleSelected}
                        onChange={toggleVisibleSelection}
                        aria-label="Select all visible users"
                      />
                    </Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Subject</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Roles</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>

                <Table.Body>
                  {filteredUsers.map((user) => (
                    <NavigableTableRow
                      key={user.subject}
                      to={`/users/${encodeURIComponent(user.subject)}`}
                      label={`user ${user.displayName ?? user.subject}`}
                    >
                      <Table.Cell>
                        <input
                          type="checkbox"
                          checked={isSelected(user.subject)}
                          onChange={() => toggleSelection(user.subject)}
                          aria-label={`Select user ${user.subject}`}
                        />
                      </Table.Cell>
                      <Table.RowHeaderCell>{user.subject}</Table.RowHeaderCell>
                      <Table.Cell>{user.displayName ?? '-'}</Table.Cell>
                      <Table.Cell>
                        <Flex gap="2" wrap="wrap">
                          {user.roles.map((role) => (
                            <Badge key={role} variant="soft">
                              {role}
                            </Badge>
                          ))}
                        </Flex>
                      </Table.Cell>
                      <Table.Cell>
                        <div className="table-row-actions">
                          <TableIconButton
                            label={rowActionSubject === user.subject ? 'Deleting user' : 'Delete user'}
                            color="red"
                            disabled={!canManageRoles || rowActionSubject === user.subject}
                            onClick={() => {
                              void handleDelete(user.subject)
                            }}
                          >
                            <TrashIcon width={15} height={15} />
                          </TableIconButton>
                          <DetailAction to={`/users/${encodeURIComponent(user.subject)}`} label={`Open ${user.displayName ?? user.subject}`} />
                        </div>
                      </Table.Cell>
                    </NavigableTableRow>
                  ))}
                </Table.Body>
              </Table.Root>
            </div>
          </ListFeedback>
        </Flex>
      </Card>
    </Flex>
  )
}
