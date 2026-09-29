import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createSource, deleteSource, disableSource, enableSource, getSourceById, listAllSources, updateSource,
  type SourceInput,
} from '../../services/api/sources-api'

export function useSourcesQuery(enabled = true) {
  return useQuery({
    queryKey: ['sources'],
    queryFn: ({ signal }) => listAllSources({ signal }),
    enabled,
  })
}

export function useSourceDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['sources', 'detail', id],
    queryFn: ({ signal }) => getSourceById(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useCreateSourceMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: SourceInput) => createSource(input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['sources'] }),
  })
}

export function useUpdateSourceMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: SourceInput }) => updateSource(id, input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['sources'] }),
  })
}

export function useToggleSourceMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => enabled ? enableSource(id) : disableSource(id),
    onSuccess: () => client.invalidateQueries({ queryKey: ['sources'] }),
  })
}

export function useDeleteSourceMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteSource(id),
    onSuccess: () => client.invalidateQueries({ queryKey: ['sources'] }),
  })
}
