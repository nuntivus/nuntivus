import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  getDeliveryAttempts,
  getDeliveryById,
  getDeliveryFailure,
  listDeliveries,
  requeueDelivery,
  retryDelivery,
  type DeliveryListQuery,
  type ManualDeliveryOperationRequest,
} from '../../services/api/deliveries-api'

export function useDeliveriesQuery(query: DeliveryListQuery) {
  return useQuery({
    queryKey: ['deliveries', query],
    queryFn: ({ signal }) => listDeliveries(query, { signal }),
  })
}

export function useFailedDeliveriesQuery(query: DeliveryListQuery) {
  return useQuery({
    queryKey: ['deliveries', 'failed', query],
    queryFn: ({ signal }) =>
      listDeliveries({
        ...query,
        status: query.status ?? 'Failed',
      }, { signal }),
  })
}

export function useDeliveryDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id],
    queryFn: ({ signal }) => getDeliveryById(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useDeliveryFailureQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id, 'failure'],
    queryFn: ({ signal }) => getDeliveryFailure(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useDeliveryAttemptsQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id, 'attempts'],
    queryFn: ({ signal }) => getDeliveryAttempts(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useRetryDeliveryMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ManualDeliveryOperationRequest) => retryDelivery(id, request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['deliveries'],
        ['deliveries', 'detail', id],
        ['deliveries', 'detail', id, 'failure'],
        ['deliveries', 'detail', id, 'attempts'],
      ])
    },
  })
}

export function useRequeueDeliveryMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ManualDeliveryOperationRequest) => requeueDelivery(id, request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['deliveries'],
        ['deliveries', 'detail', id],
        ['deliveries', 'detail', id, 'failure'],
        ['deliveries', 'detail', id, 'attempts'],
      ])
    },
  })
}
