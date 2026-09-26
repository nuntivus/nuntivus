import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  getInstanceSettings,
  updateInstanceSettings,
  type UpdateInstanceSettingsRequest,
} from '../../services/api/settings-api'

export function useInstanceSettingsQuery(enabled = true) {
  return useQuery({
    queryKey: ['settings', 'instance'],
    queryFn: ({ signal }) => getInstanceSettings({ signal }),
    enabled,
  })
}

export function useUpdateInstanceSettingsMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateInstanceSettingsRequest) => updateInstanceSettings(request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [['settings', 'instance']])
    },
  })
}
