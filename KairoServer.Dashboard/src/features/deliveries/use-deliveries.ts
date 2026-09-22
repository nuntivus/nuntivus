import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
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
    queryFn: () => listDeliveries(query),
  })
}

export function useFailedDeliveriesQuery(query: DeliveryListQuery) {
  return useQuery({
    queryKey: ['deliveries', 'failed', query],
    queryFn: () =>
      listDeliveries({
        ...query,
        status: query.status ?? 'Failed',
      }),
  })
}

export function useDeliveryDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id],
    queryFn: () => getDeliveryById(id ?? ''),
    enabled: !!id,
  })
}

export function useDeliveryFailureQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id, 'failure'],
    queryFn: () => getDeliveryFailure(id ?? ''),
    enabled: !!id,
  })
}

export function useDeliveryAttemptsQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['deliveries', 'detail', id, 'attempts'],
    queryFn: () => getDeliveryAttempts(id ?? ''),
    enabled: !!id,
  })
}

export function useRetryDeliveryMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ManualDeliveryOperationRequest) => retryDelivery(id, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['deliveries'] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id, 'failure'] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id, 'attempts'] }),
      ])
    },
  })
}

export function useRequeueDeliveryMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ManualDeliveryOperationRequest) => requeueDelivery(id, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['deliveries'] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id, 'failure'] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries', 'detail', id, 'attempts'] }),
      ])
    },
  })
}
