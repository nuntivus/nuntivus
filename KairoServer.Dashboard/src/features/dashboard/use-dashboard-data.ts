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
    queryFn: getDashboardOverview,
  })
}

export function useSuccessfulDeliveriesCountQuery() {
  return useQuery({
    queryKey: ['dashboard', 'deliveries', 'successful-count'],
    queryFn: getSuccessfulDeliveriesCount,
  })
}

export function useRecentEventsQuery() {
  return useQuery({
    queryKey: ['dashboard', 'events', 'recent'],
    queryFn: () => getRecentEvents(6),
  })
}

export function useRecentFailuresQuery() {
  return useQuery({
    queryKey: ['dashboard', 'failures', 'recent'],
    queryFn: () => getRecentFailures(6),
  })
}
