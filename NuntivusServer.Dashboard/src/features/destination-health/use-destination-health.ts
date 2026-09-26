import { useQuery } from '@tanstack/react-query'
import {
  getDestinationHealthByContractId,
  listDestinationHealthByCustomerId,
} from '../../services/api/destination-health-api'

export function useContractDestinationHealthQuery(contractId: string | undefined) {
  return useQuery({
    queryKey: ['destination-health', 'contracts', contractId],
    queryFn: ({ signal }) => getDestinationHealthByContractId(contractId ?? '', {
      signal,
      suppressErrorStatuses: [404],
    }),
    enabled: !!contractId,
  })
}

export function useCustomerDestinationHealthQuery(customerId: string | undefined) {
  return useQuery({
    queryKey: ['destination-health', 'customers', customerId],
    queryFn: ({ signal }) => listDestinationHealthByCustomerId(customerId ?? '', { signal }),
    enabled: !!customerId,
  })
}
