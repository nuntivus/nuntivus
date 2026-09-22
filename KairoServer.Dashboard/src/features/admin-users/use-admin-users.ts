import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
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
    queryFn: listAdminUsers,
  })
}

export function useAdminUserQuery(subject: string | undefined) {
  return useQuery({
    queryKey: ['admin', 'users', subject],
    queryFn: () => getAdminUser(subject ?? ''),
    enabled: !!subject,
  })
}

export function useAdminRolesQuery() {
  return useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: listAdminRoles,
  })
}

export function useCreateAdminUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateAdminUserRequest) => createAdminUser(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] })
    },
  })
}

export function useReplaceAdminUserRolesMutation(subject: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ReplaceAdminUserRolesRequest) =>
      replaceAdminUserRoles(subject, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['admin', 'users'] }),
        queryClient.invalidateQueries({ queryKey: ['admin', 'users', subject] }),
      ])
    },
  })
}

export function useDeleteAdminUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (subject: string) => deleteAdminUser(subject),
    onSuccess: async (_, subject) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['admin', 'users'] }),
        queryClient.invalidateQueries({ queryKey: ['admin', 'users', subject] }),
      ])
    },
  })
}
