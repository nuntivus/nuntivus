import {
  Badge,
  Box,
  Button,
  Card,
  Flex,
  Heading,
  Link,
  Spinner,
  Table,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useAdminRolesQuery,
  useAdminUsersQuery,
  useCreateAdminUserMutation,
} from '../features/admin-users/use-admin-users'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

export function UsersPage() {
  const { currentUser } = useAppSelector((state) => state.auth)
  const canReadUsers = hasPermission(currentUser, 'users.read')
  const canManageRoles = hasPermission(currentUser, 'roles.manage')

  const usersQuery = useAdminUsersQuery()
  const rolesQuery = useAdminRolesQuery()
  const createUserMutation = useCreateAdminUserMutation()

  const [subject, setSubject] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [selectedRoles, setSelectedRoles] = useState<string[]>(['viewer'])

  const users = usersQuery.data ?? []
  const createError = useMemo(
    () => getApiErrorMessage(createUserMutation.error, 'Unable to create user'),
    [createUserMutation.error],
  )

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

    if (!canManageRoles || !subject.trim() || selectedRoles.length === 0) {
      return
    }

    await createUserMutation.mutateAsync({
      subject: subject.trim(),
      displayName: displayName.trim() || null,
      roles: selectedRoles,
    })

    setSubject('')
    setDisplayName('')
    setSelectedRoles(['viewer'])
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
    <Flex direction="column" gap="4">
      <Heading size="6">Users & RBAC</Heading>

      {canManageRoles ? (
        <Card>
          <form onSubmit={handleCreate}>
            <Flex direction="column" gap="3">
              <Heading size="4">Invite or create user</Heading>
              <TextField.Root
                placeholder="Subject (user id or email)"
                value={subject}
                onChange={(event) => setSubject(event.target.value)}
              />
              <TextField.Root
                placeholder="Display name (optional)"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
              />

              <Box>
                <Text size="2" weight="medium">
                  Roles
                </Text>
                {rolesQuery.isLoading ? (
                  <Flex align="center" gap="2" mt="2">
                    <Spinner size="1" />
                    <Text size="2">Loading roles...</Text>
                  </Flex>
                ) : (
                  <Flex direction="column" gap="2" mt="2">
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
                )}
              </Box>

              {createUserMutation.isError ? (
                <Text size="2" color="red">
                  {createError}
                </Text>
              ) : null}

              <Button
                type="submit"
                disabled={
                  createUserMutation.isPending ||
                  !subject.trim() ||
                  selectedRoles.length === 0
                }
              >
                {createUserMutation.isPending ? 'Creating...' : 'Create user'}
              </Button>
            </Flex>
          </form>
        </Card>
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

          {usersQuery.isLoading ? (
            <Flex align="center" gap="2">
              <Spinner size="1" />
              <Text size="2">Loading users...</Text>
            </Flex>
          ) : usersQuery.isError ? (
            <Text color="red" size="2">
              {forbiddenFromApi
                ? 'The backend denied access (403).'
                : getApiErrorMessage(usersQuery.error, 'Unable to load users.')}
            </Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>Subject</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Roles</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>

              <Table.Body>
                {users.map((user) => (
                  <Table.Row key={user.subject}>
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
                      <Link asChild>
                        <NavLink to={`/users/${encodeURIComponent(user.subject)}`}>
                          View detail
                        </NavLink>
                      </Link>
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
