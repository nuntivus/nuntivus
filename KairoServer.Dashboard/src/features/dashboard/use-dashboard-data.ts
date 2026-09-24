import { useQuery } from '@tanstack/react-query'
import {
  getDashboardOverview,
  getRecentEvents,
  getRecentFailures,
  getSuccessfulDeliveriesCount,
} from '../../services/api/dashboard-api'

export function useDashboardOverviewQuery() {
  return useQuery({
    queryKey: ['dashboard', 'overview'],
    queryFn: ({ signal }) => getDashboardOverview({ signal }),
  })
}

export function useSuccessfulDeliveriesCountQuery() {
  return useQuery({
    queryKey: ['dashboard', 'deliveries', 'successful-count'],
    queryFn: ({ signal }) => getSuccessfulDeliveriesCount({ signal }),
  })
}

export function useRecentEventsQuery() {
  return useQuery({
    queryKey: ['dashboard', 'events', 'recent'],
    queryFn: ({ signal }) => getRecentEvents(6, { signal }),
  })
}

export function useRecentFailuresQuery() {
  return useQuery({
    queryKey: ['dashboard', 'failures', 'recent'],
    queryFn: ({ signal }) => getRecentFailures(6, { signal }),
  })
}
