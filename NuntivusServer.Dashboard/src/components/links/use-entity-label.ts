import { useQuery } from '@tanstack/react-query'
import { getContractById } from '../../services/api/contracts-api'
import { getCustomerById } from '../../services/api/customers-api'
import { getDestinationById } from '../../services/api/destinations-api'
import { getEventById } from '../../services/api/events-api'
import { getSourceById } from '../../services/api/sources-api'

export type EntityKind = 'customer' | 'contract' | 'event' | 'source' | 'destination'

async function loadEntityLabel(kind: EntityKind, id: string, signal?: AbortSignal) {
  const options = { signal }
  switch (kind) {
    case 'customer': return (await getCustomerById(id, options)).name
    case 'contract': return (await getContractById(id, options)).contract.name ?? null
    case 'event': return (await getEventById(id, options)).type
    case 'source': return (await getSourceById(id, options)).name
    case 'destination': return (await getDestinationById(id, options)).name
  }
}

export function useEntityLabel(kind: EntityKind, id: string | null | undefined, label?: string | null) {
  const query = useQuery({
    queryKey: ['entity-label', kind, id],
    queryFn: ({ signal }) => loadEntityLabel(kind, id ?? '', signal),
    enabled: !!id && !label?.trim(),
    retry: false,
    staleTime: 60_000,
  })

  return label?.trim() || query.data?.trim() || null
}
