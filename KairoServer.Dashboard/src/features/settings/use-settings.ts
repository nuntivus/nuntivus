import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getInstanceSettings,
  updateInstanceSettings,
  type UpdateInstanceSettingsRequest,
} from '../../services/api/settings-api'

export function useInstanceSettingsQuery(enabled = true) {
  return useQuery({
    queryKey: ['settings', 'instance'],
    queryFn: () => getInstanceSettings(),
    enabled,
  })
}

export function useUpdateInstanceSettingsMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateInstanceSettingsRequest) => updateInstanceSettings(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['settings', 'instance'] })
    },
  })
}
