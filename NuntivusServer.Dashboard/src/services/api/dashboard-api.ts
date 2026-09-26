import { apiGet, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export interface DashboardOverview {
  asOf: string
  totalCustomers: number
  totalPendingDeliveries: number
  totalFailedDeliveries: number
  totalDeadDeliveries: number
}

export interface DashboardTimeBucket {
  start: string
  events: number
  deliveries: number
  delivered: number
  failed: number
  dead: number
}

export interface DashboardTimeSeries {
  asOf: string
  rangeSeconds: number
  buckets: DashboardTimeBucket[]
}

export interface EventListItem {
  id: string
  idempotencyKey: string
  type: string
  source: string
  occurredAt: string
  receivedAt: string
}

export interface FailedDeliveryItem {
  deliveryId: string
  eventId: string
  contractId: string
  customerId: string
  eventType: string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export async function getDashboardOverview(options?: ApiRequestOptions) {
  return apiGet<DashboardOverview>('/v1/admin/overview', undefined, options)
}

export async function getDashboardTimeSeries(rangeSeconds: number, options?: ApiRequestOptions) {
  return apiGet<DashboardTimeSeries>('/v1/admin/overview/timeseries', { rangeSeconds }, options)
}

export async function getSuccessfulDeliveriesCount(options?: ApiRequestOptions) {
  const response = await apiGet<PagedResponse<unknown>>('/v1/deliveries', {
    status: 'Delivered',
    page: 1,
    pageSize: 1,
  }, options)

  return response.totalCount
}

export async function getRecentEvents(pageSize = 5, options?: ApiRequestOptions) {
  const response = await apiGet<PagedResponse<EventListItem>>('/v1/events', {
    page: 1,
    pageSize,
  }, options)

  return response.items
}

export async function getRecentFailures(pageSize = 5, options?: ApiRequestOptions) {
  const response = await apiGet<PagedResponse<FailedDeliveryItem>>(
    '/v1/admin/deliveries/failures',
    {
      page: 1,
      pageSize,
    },
    options,
  )

  return response.items
}
