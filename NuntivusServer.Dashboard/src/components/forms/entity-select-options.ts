import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { getContractById, listContracts } from '../../services/api/contracts-api'
import { getCustomerById, listCustomers } from '../../services/api/customers-api'
import { getDeliveryById, listDeliveries } from '../../services/api/deliveries-api'
import { getEventById, listEvents } from '../../services/api/events-api'
import { getSourceById, listAllSources } from '../../services/api/sources-api'

export type SelectableEntityKind = 'customer' | 'contract' | 'event' | 'delivery' | 'source'
export type EntityOption = { id: string; label: string }

export const entityReadPermissions: Record<SelectableEntityKind, string> = {
  customer: 'customers.read',
  contract: 'contracts.read',
  event: 'events.read',
  delivery: 'deliveries.read',
  source: 'contracts.read',
}

async function loadOptions(kind: Exclude<SelectableEntityKind, 'source'>, search: string, page: number, signal: AbortSignal) {
  const query = { search, page, pageSize: 25 }
  const options = { signal }
  switch (kind) {
    case 'customer': {
      const result = await listCustomers(query, options)
      return { items: result.items.map(item => ({ id: item.id, label: item.name })), hasNext: result.hasNext }
    }
    case 'contract': {
      const result = await listContracts(query, options)
      return { items: result.items.map(item => ({ id: item.id, label: item.name?.trim() || item.id })), hasNext: result.hasNext }
    }
    case 'event': {
      const result = await listEvents(query, options)
      return { items: result.items.map(item => ({ id: item.id, label: item.type })), hasNext: result.hasNext }
    }
    case 'delivery': {
      const result = await listDeliveries(query, options)
      return { items: result.items.map(item => ({ id: item.id, label: item.eventType })), hasNext: result.hasNext }
    }
  }
}

async function loadSelectedOption(kind: SelectableEntityKind, id: string, signal: AbortSignal): Promise<EntityOption> {
  const options = { signal }
  switch (kind) {
    case 'customer': return { id, label: (await getCustomerById(id, options)).name }
    case 'contract': return { id, label: (await getContractById(id, options)).contract.name?.trim() || id }
    case 'event': return { id, label: (await getEventById(id, options)).type }
    case 'delivery': return { id, label: (await getDeliveryById(id, options)).eventType }
    case 'source': return { id, label: (await getSourceById(id, options)).name }
  }
}

export function useEntityOptions(kind: SelectableEntityKind, search: string, value: string, enabled: boolean, open: boolean) {
  const sources = useQuery({
    queryKey: ['sources', 'all'],
    queryFn: ({ signal }) => listAllSources({ signal }),
    enabled: enabled && open && kind === 'source',
    staleTime: 60_000,
  })
  const results = useInfiniteQuery({
    queryKey: ['entity-options', kind, search],
    queryFn: ({ pageParam, signal }) => loadOptions(kind as Exclude<SelectableEntityKind, 'source'>, search, pageParam, signal),
    initialPageParam: 1,
    getNextPageParam: (lastPage, pages) => lastPage.hasNext ? pages.length + 1 : undefined,
    enabled: enabled && open && kind !== 'source',
    staleTime: 60_000,
  })
  const options = kind === 'source'
    ? (sources.data ?? []).filter(item => `${item.name} ${item.id}`.toLowerCase().includes(search.toLowerCase())).map(item => ({ id: item.id, label: item.name }))
    : results.data?.pages.flatMap(page => page.items) ?? []
  const knownValue = options.find(item => item.id === value)
  const selected = useQuery({
    queryKey: ['entity-option', kind, value],
    queryFn: ({ signal }) => loadSelectedOption(kind, value, signal),
    enabled: enabled && !!value && !knownValue,
    retry: false,
    staleTime: 60_000,
  })
  return {
    options: [...new Map(options.map(item => [item.id, item])).values()],
    selected: knownValue ?? selected.data,
    isLoading: kind === 'source' ? sources.isLoading : results.isLoading,
    error: kind === 'source' ? sources.error : results.error,
    hasNextPage: kind !== 'source' && results.hasNextPage,
    isFetchingNextPage: results.isFetchingNextPage,
    fetchNextPage: results.fetchNextPage,
    retry: kind === 'source' ? sources.refetch : results.refetch,
  }
}
