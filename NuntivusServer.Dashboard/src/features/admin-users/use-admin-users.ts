import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  createAdminUser,
  deleteAdminUser,
  getAdminUser,
  listAdminUsers,
  replaceAdminUserRoles,
  type CreateAdminUserRequest,
  type ReplaceAdminUserRolesRequest,
} from '../../services/api/admin-users-api'
import { listAdminRoles } from '../../services/api/admin-roles-api'

export function useAdminUsersQuery() {
  return useQuery({
    queryKey: ['admin', 'users'],
    queryFn: ({ signal }) => listAdminUsers({ signal }),
  })
}

export function useAdminUserQuery(subject: string | undefined) {
  return useQuery({
    queryKey: ['admin', 'users', subject],
    queryFn: ({ signal }) => getAdminUser(subject ?? '', { signal }),
    enabled: !!subject,
  })
}

export function useAdminRolesQuery() {
  return useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: ({ signal }) => listAdminRoles({ signal }),
  })
}

export function useCreateAdminUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateAdminUserRequest) => createAdminUser(request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [['admin', 'users']])
    },
  })
}

export function useReplaceAdminUserRolesMutation(subject: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ReplaceAdminUserRolesRequest) =>
      replaceAdminUserRoles(subject, request),
    onSuccess: async (user) => {
      queryClient.setQueryData(['admin', 'users', subject], user)
      await queryClient.invalidateQueries({
        queryKey: ['admin', 'users'],
        exact: true,
      })
    },
  })
}

export function useDeleteAdminUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (subject: string) => deleteAdminUser(subject),
    onSuccess: async (_, subject) => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'], exact: true })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users', subject], exact: true, refetchType: 'none' })
    },
  })
}
