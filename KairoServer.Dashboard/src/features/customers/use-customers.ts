import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createCustomer,
  disableCustomer,
  enableCustomer,
  getCustomerById,
  listCustomerContracts,
  listCustomerDeliveries,
  listCustomerRecentFailures,
  listCustomers,
  updateCustomer,
  type CreateCustomerRequest,
  type CustomerListQuery,
  type UpdateCustomerRequest,
} from '../../services/api/customers-api'

export function useCustomersQuery(query: CustomerListQuery) {
  return useQuery({
    queryKey: ['customers', query],
    queryFn: () => listCustomers(query),
  })
}

export function useCustomerDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['customers', 'detail', id],
    queryFn: () => getCustomerById(id ?? ''),
    enabled: !!id,
  })
}

export function useCustomerContractsQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['customers', 'detail', id, 'contracts'],
    queryFn: () => listCustomerContracts(id ?? ''),
    enabled: !!id,
  })
}

export function useCustomerDeliveriesQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['customers', 'detail', id, 'deliveries'],
    queryFn: () => listCustomerDeliveries(id ?? ''),
    enabled: !!id,
  })
}

export function useCustomerRecentFailuresQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['customers', 'detail', id, 'failures'],
    queryFn: () => listCustomerRecentFailures(id ?? ''),
    enabled: !!id,
  })
}

export function useCreateCustomerMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateCustomerRequest) => createCustomer(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['customers'] })
    },
  })
}

export function useUpdateCustomerMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateCustomerRequest) => updateCustomer(id, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['customers'] }),
        queryClient.invalidateQueries({ queryKey: ['customers', 'detail', id] }),
      ])
    },
  })
}

export function useEnableCustomerMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => enableCustomer(id),
    onSuccess: async (_, id) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['customers'] }),
        queryClient.invalidateQueries({ queryKey: ['customers', 'detail', id] }),
      ])
    },
  })
}

export function useDisableCustomerMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => disableCustomer(id),
    onSuccess: async (_, id) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['customers'] }),
        queryClient.invalidateQueries({ queryKey: ['customers', 'detail', id] }),
      ])
    },
  })
}
