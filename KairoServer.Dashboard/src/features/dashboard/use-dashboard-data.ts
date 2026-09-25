import { useQuery } from '@tanstack/react-query'
import {
  getDashboardOverview,
  getDashboardTimeSeries,
  getRecentEvents,
  getRecentFailures,
  getSuccessfulDeliveriesCount,
} from '../../services/api/dashboard-api'

export function useDashboardOverviewQuery(refreshInterval: number) {
  return useQuery({
    queryKey: ['dashboard', 'overview'],
    queryFn: ({ signal }) => getDashboardOverview({ signal }),
    refetchInterval: refreshInterval,
  })
}

export function useDashboardTimeSeriesQuery(rangeSeconds: number, refreshInterval: number) {
  return useQuery({
    queryKey: ['dashboard', 'timeseries', rangeSeconds],
    queryFn: ({ signal }) => getDashboardTimeSeries(rangeSeconds, { signal }),
    refetchInterval: refreshInterval,
  })
}

export function useSuccessfulDeliveriesCountQuery(refreshInterval: number) {
  return useQuery({
    queryKey: ['dashboard', 'deliveries', 'successful-count'],
    queryFn: ({ signal }) => getSuccessfulDeliveriesCount({ signal }),
    refetchInterval: refreshInterval,
  })
}

export function useRecentEventsQuery(refreshInterval: number) {
  return useQuery({
    queryKey: ['dashboard', 'events', 'recent'],
    queryFn: ({ signal }) => getRecentEvents(6, { signal }),
    refetchInterval: refreshInterval,
  })
}

export function useRecentFailuresQuery(refreshInterval: number) {
  return useQuery({
    queryKey: ['dashboard', 'failures', 'recent'],
    queryFn: ({ signal }) => getRecentFailures(6, { signal }),
    refetchInterval: refreshInterval,
  })
}
