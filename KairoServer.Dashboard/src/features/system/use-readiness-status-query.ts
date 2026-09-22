import { useQuery } from '@tanstack/react-query'
import { getReadinessStatus } from '../../services/api/system-api'

export function useReadinessStatusQuery() {
  return useQuery({
    queryKey: ['system', 'health', 'ready'],
    queryFn: getReadinessStatus,
  })
}
