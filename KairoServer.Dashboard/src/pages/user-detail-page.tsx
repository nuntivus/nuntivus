import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Spinner,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useState } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useAdminRolesQuery,
  useAdminUserQuery,
  useDeleteAdminUserMutation,
  useReplaceAdminUserRolesMutation,
} from '../features/admin-users/use-admin-users'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

export function UserDetailPage() {
  const { subject } = useParams()
  const navigate = useNavigate()
  const { currentUser } = useAppSelector((state) => state.auth)
  const canReadUsers = hasPermission(currentUser, 'users.read')
  const canManageRoles = hasPermission(currentUser, 'roles.manage')

  const userQuery = useAdminUserQuery(subject)
  const rolesQuery = useAdminRolesQuery()
  const replaceRolesMutation = useReplaceAdminUserRolesMutation(subject ?? '')
  const deleteMutation = useDeleteAdminUserMutation()

  const user = userQuery.data

  if (!canReadUsers) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">User detail</Heading>
          <Text color="gray">You do not have permission to read users.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  if (!subject) {
    return (
      <Card>
        <Text color="red">Invalid user subject.</Text>
      </Card>
    )
  }

  if (userQuery.isLoading) {
    return (
      <Flex align="center" gap="2">
        <Spinner size="1" />
        <Text>Loading user detail...</Text>
      </Flex>
    )
  }

  if (userQuery.isError) {
    const error = userQuery.error

    if (error instanceof ApiError && error.status === 403) {
      return (
        <Card>
          <Text color="red">Access denied by backend (403).</Text>
        </Card>
      )
    }

    if (error instanceof ApiError && error.status === 404) {
      return (
        <Card>
          <Text>User not found.</Text>
        </Card>
      )
    }

    return (
      <Card>
        <Text color="red">{getApiErrorMessage(error, 'Unable to load user.')}</Text>
      </Card>
    )
  }

  if (!user) {
    return null
  }

  const mutationError =
    getApiErrorMessage(replaceRolesMutation.error, '') ||
    getApiErrorMessage(deleteMutation.error, '')

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">User detail</Heading>

      <UserDetailEditor
        key={`${user.subject}:${user.updatedAt}`}
        subject={subject}
        displayName={user.displayName}
        roles={user.roles}
        canManageRoles={canManageRoles}
        availableRoles={rolesQuery.data ?? []}
        loadingRoles={rolesQuery.isLoading}
        savePending={replaceRolesMutation.isPending}
        deletePending={deleteMutation.isPending}
        mutationError={mutationError}
        onSave={(payload) => replaceRolesMutation.mutateAsync(payload)}
        onDelete={async () => {
          await deleteMutation.mutateAsync(subject)
          navigate('/users')
        }}
      />
    </Flex>
  )
}

interface UserDetailEditorProps {
  subject: string
  displayName: string | null
  roles: string[]
  canManageRoles: boolean
  availableRoles: Array<{ name: string }>
  loadingRoles: boolean
  savePending: boolean
  deletePending: boolean
  mutationError: string
  onSave: (payload: { displayName: string | null; roles: string[] }) => Promise<unknown>
  onDelete: () => Promise<void>
}

function UserDetailEditor({
  subject,
  displayName,
  roles,
  canManageRoles,
  availableRoles,
  loadingRoles,
  savePending,
  deletePending,
  mutationError,
  onSave,
  onDelete,
}: UserDetailEditorProps) {
  const [editorDisplayName, setEditorDisplayName] = useState(displayName ?? '')
  const [selectedRoles, setSelectedRoles] = useState<string[]>(roles)

  function toggleRole(roleName: string) {
    setSelectedRoles((current) => {
      if (current.includes(roleName)) {
        return current.filter((value) => value !== roleName)
      }

      return [...current, roleName]
    })
  }

  return (
    <Card>
      <Flex direction="column" gap="3">
        <Text size="2" color="gray">
          Subject
        </Text>
        <Text weight="medium">{subject}</Text>

        <Text size="2" color="gray">
          Display name
        </Text>
        <TextField.Root
          value={editorDisplayName}
          onChange={(event) => setEditorDisplayName(event.target.value)}
          disabled={!canManageRoles}
        />

        <Text size="2" color="gray">
          Roles
        </Text>
        {loadingRoles ? (
          <Flex align="center" gap="2">
            <Spinner size="1" />
            <Text>Loading roles...</Text>
          </Flex>
        ) : (
          <Flex direction="column" gap="2">
            {availableRoles.map((role) => (
              <label key={role.name}>
                <input
                  type="checkbox"
                  checked={selectedRoles.includes(role.name)}
                  disabled={!canManageRoles}
                  onChange={() => toggleRole(role.name)}
                />{' '}
                {role.name}
              </label>
            ))}
          </Flex>
        )}

        <Flex gap="2" wrap="wrap">
          {selectedRoles.map((role) => (
            <Badge key={role} variant="soft">
              {role}
            </Badge>
          ))}
        </Flex>

        {mutationError ? (
          <Text color="red" size="2">
            {mutationError}
          </Text>
        ) : null}

        <Flex gap="3">
          <Button
            type="button"
            onClick={() =>
              onSave({
                displayName: editorDisplayName.trim() || null,
                roles: selectedRoles,
              })
            }
            disabled={!canManageRoles || savePending || selectedRoles.length === 0}
          >
            {savePending ? 'Saving...' : 'Save roles'}
          </Button>
          <Button
            type="button"
            color="red"
            variant="soft"
            onClick={onDelete}
            disabled={!canManageRoles || deletePending}
          >
            {deletePending ? 'Deleting...' : 'Delete user'}
          </Button>
        </Flex>
      </Flex>
    </Card>
  )
}
