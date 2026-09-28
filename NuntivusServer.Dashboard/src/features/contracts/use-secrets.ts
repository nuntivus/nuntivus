import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createSecret, deleteSecret, listSecrets, listSecretVersions, updateSecret } from '../../services/api/secrets-api'

export function useSecretsQuery() {
  return useQuery({
    queryKey: ['secrets'],
    queryFn: ({ signal }) => listSecrets({ signal }),
  })
}

export function useSecretVersionsQuery(name: string | null) {
  return useQuery({
    queryKey: ['secrets', name, 'versions'],
    queryFn: ({ signal }) => listSecretVersions(name ?? '', { signal }),
    enabled: !!name,
  })
}

export function useUpdateSecretMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) => updateSecret(name, value),
    onSuccess: async (_, { name }) => {
      await queryClient.invalidateQueries({ queryKey: ['secrets'] })
      await queryClient.invalidateQueries({ queryKey: ['secrets', name, 'versions'] })
    },
  })
}

export function useDeleteSecretMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => deleteSecret(name),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['secrets'] })
    },
  })
}

export function useCreateSecretMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) => createSecret(name, value),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['secrets'] })
    },
  })
}
