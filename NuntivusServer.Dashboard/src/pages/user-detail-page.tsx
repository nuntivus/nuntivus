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
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { CalendarIcon, CheckCircleIcon, SettingsIcon, UsersIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailMetric, DetailSectionTitle } from '../components/states/detail-visuals'
import {
  useAdminRolesQuery,
  useAdminUserQuery,
  useDeleteAdminUserMutation,
  useReplaceAdminUserRolesMutation,
} from '../features/admin-users/use-admin-users'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

export function UserDetailPage() {
  const { subject } = useParams()
  const navigate = useNavigate()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
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
    <Flex direction="column" gap="4" className="detail-page">
      <DetailPageHeader
        title={user.displayName || subject}
        description="Identity profile, role assignments, and account access."
        icon={UsersIcon}
        actions={(
          <Button asChild variant="soft">
            <NavLink to="/users">Back to list</NavLink>
          </Button>
        )}
      />

      <div className="visual-detail-metric-grid">
        <DetailMetric icon={UsersIcon} label="Subject" value={user.subject} supporting="Identity provider subject" />
        <DetailMetric icon={CheckCircleIcon} label="Assigned roles" value={user.roles.length} supporting={user.roles.join(', ') || 'No roles assigned'} tone="success" />
        <DetailMetric icon={SettingsIcon} label="Access" value={canManageRoles ? 'Manageable' : 'Read only'} supporting="Current operator permissions" tone="violet" />
        <DetailMetric icon={CalendarIcon} label="Updated" value={new Date(user.updatedAt).toLocaleString()} supporting={`Created ${new Date(user.createdAt).toLocaleDateString()}`} />
      </div>

      <div className="detail-panel detail-panel-wide">
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
          onSave={async (payload) => {
            try {
              await replaceRolesMutation.mutateAsync(payload)
              notifications.success('User roles saved.', 'User detail')
            } catch (error) {
              notifications.error(getApiErrorMessage(error, 'Unable to save user roles.'), 'User detail')
            }
          }}
          onDelete={async () => {
            const confirmed = await confirm({
              title: 'Delete user',
              description: `Delete user "${subject}"? This permanently removes assigned roles and access until re-created.`,
              confirmText: 'Delete',
              cancelText: 'Cancel',
              confirmColor: 'red',
            })

            if (!confirmed) {
              return
            }

            try {
              await deleteMutation.mutateAsync(subject)
              notifications.success('User deleted.', 'User detail')
              navigate('/users')
            } catch (error) {
              notifications.error(getApiErrorMessage(error, 'Unable to delete user.'), 'User detail')
            }
          }}
        />
      </div>
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
  const rolesError = selectedRoles.length === 0 ? 'At least one role is required.' : null

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
        <DetailSectionTitle icon={UsersIcon}>User information</DetailSectionTitle>
        <Text size="2" color="gray">
          Subject
        </Text>
        <Text weight="medium">{subject}</Text>

        <Text as="label" htmlFor="user-display-name" size="2" color="gray">
          Display name
        </Text>
        <TextField.Root
          id="user-display-name"
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
        <FieldErrorText id="user-detail-roles-error" message={rolesError} />

        <Flex gap="2" wrap="wrap">
          {selectedRoles.map((role) => (
            <Badge key={role} variant="soft">
              {role}
            </Badge>
          ))}
        </Flex>

        <FormErrorText id="user-detail-submit-error" message={mutationError} />

        <Flex gap="3">
          <Button
            type="button"
            onClick={() =>
              onSave({
                displayName: editorDisplayName.trim() || null,
                roles: selectedRoles,
              })
            }
            disabled={!canManageRoles || savePending || !!rolesError}
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
