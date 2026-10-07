import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createDestination, createDestinationDraft, deleteDestination, disableDestination, enableDestination,
  getDestinationById, listAllDestinations, updateDestination,
  type DestinationInput, type DestinationDraftInput,
} from '../../services/api/destinations-api'

export function useDestinationsQuery(enabled = true) {
  return useQuery({ queryKey: ['destinations'], queryFn: ({ signal }) => listAllDestinations({ signal }), enabled })
}

export function useDestinationDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['destinations', 'detail', id],
    queryFn: ({ signal }) => getDestinationById(id ?? '', { signal }),
    enabled: !!id,
  })
}

function useDestinationMutation<T, TResult>(mutationFn: (input: T) => Promise<TResult>) {
  const client = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['destinations'] })
      await client.invalidateQueries({ queryKey: ['contracts'] })
    },
  })
}

export function useCreateDestinationMutation() {
  return useDestinationMutation((input: DestinationInput) => createDestination(input))
}

export function useCreateDestinationDraftMutation() {
  return useDestinationMutation((input: DestinationDraftInput) =>
    createDestinationDraft(input))
}

export function useUpdateDestinationMutation() {
  return useDestinationMutation(({ id, input }: { id: string; input: DestinationInput }) => updateDestination(id, input))
}

export function useToggleDestinationMutation() {
  return useDestinationMutation(({ id, enabled, pauseExistingBindings }: {
    id: string; enabled: boolean; pauseExistingBindings: boolean
  }) => enabled ? enableDestination(id) : disableDestination(id, pauseExistingBindings))
}

export function useDeleteDestinationMutation() {
  return useDestinationMutation(({ id, removeBindings }: { id: string; removeBindings: boolean }) =>
    deleteDestination(id, removeBindings))
}
