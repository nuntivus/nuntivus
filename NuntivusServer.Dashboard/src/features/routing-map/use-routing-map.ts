import { useQuery } from '@tanstack/react-query'
import { listContracts, type Contract } from '../../services/api/contracts-api'
import { listDestinations, type DestinationResource } from '../../services/api/destinations-api'
import { listSources, type Source } from '../../services/api/sources-api'
import type { PagedResponse } from '../../services/api/paged-response'

const pageSize = 100
const maxItems = 1000

async function loadPages<T>(loadPage: (page: number) => Promise<PagedResponse<T>>) {
  const items: T[] = []
  for (let page = 1; page <= maxItems / pageSize; page++) {
    const result = await loadPage(page)
    items.push(...result.items)
    if (!result.hasNext) return { items, truncated: false }
  }
  return { items, truncated: true }
}

export interface RoutingMapData {
  contracts: Contract[]
  sources: Source[]
  destinations: DestinationResource[]
  truncated: boolean
}

export function useRoutingMapQuery() {
  return useQuery({
    queryKey: ['routing-map'],
    queryFn: async ({ signal }): Promise<RoutingMapData> => {
      const [contracts, sources, destinations] = await Promise.all([
        loadPages((page) => listContracts({ page, pageSize }, { signal })),
        loadPages((page) => listSources(page, pageSize, { signal })),
        loadPages((page) => listDestinations(page, pageSize, { signal })),
      ])
      return {
        contracts: contracts.items,
        sources: sources.items,
        destinations: destinations.items,
        truncated: contracts.truncated || sources.truncated || destinations.truncated,
      }
    },
  })
}
