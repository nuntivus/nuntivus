import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  createContract,
  disableContract,
  enableContract,
  getContractById,
  listContractDeliveries,
  listContractRecentFailures,
  listContracts,
  replaceContract,
  type ContractListQuery,
  type CreateContractRequest,
  type UpdateContractRequest,
} from '../../services/api/contracts-api'

export function useContractsQuery(query: ContractListQuery) {
  return useQuery({
    queryKey: ['contracts', query],
    queryFn: ({ signal }) => listContracts(query, { signal }),
  })
}

export function useContractDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['contracts', 'detail', id],
    queryFn: ({ signal }) => getContractById(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useContractDeliveriesQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['contracts', 'detail', id, 'deliveries'],
    queryFn: ({ signal }) => listContractDeliveries(id ?? '', 10, { signal }),
    enabled: !!id,
  })
}

export function useContractRecentFailuresQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['contracts', 'detail', id, 'failures'],
    queryFn: ({ signal }) => listContractRecentFailures(id ?? '', 10, { signal }),
    enabled: !!id,
  })
}

export function useCreateContractMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateContractRequest) => createContract(request),
    onSuccess: async (_, id) => {
      await invalidateQueryKeys(queryClient, [
        ['contracts'],
        ['contracts', 'detail', id],
      ])
    },
  })
}

export function useEnableContractMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => enableContract(id),
    onSuccess: async (_, id) => {
      await invalidateQueryKeys(queryClient, [
        ['contracts'],
        ['contracts', 'detail', id],
      ])
    },
  })
}

export function useReplaceContractMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: {
      etag: string
      payload: UpdateContractRequest
    }) => replaceContract(id, request.etag, request.payload),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['contracts'],
        ['contracts', 'detail', id],
      ])
    },
  })
}

export function useDisableContractMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => disableContract(id),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [['contracts']])
    },
  })
}
