import { apiGet } from './api-client'

export interface DashboardOverview {
  asOf: string
  totalCustomers: number
  totalPendingDeliveries: number
  totalFailedDeliveries: number
  totalDeadDeliveries: number
}

export interface PagedResponse<TItem> {
  items: TItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
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

export async function getDashboardOverview() {
  return apiGet<DashboardOverview>('/v1/admin/overview')
}

export async function getSuccessfulDeliveriesCount() {
  const response = await apiGet<PagedResponse<unknown>>('/v1/deliveries', {
    status: 'Delivered',
    page: 1,
    pageSize: 1,
  })

  return response.totalCount
}

export async function getRecentEvents(pageSize = 5) {
  const response = await apiGet<PagedResponse<EventListItem>>('/v1/events', {
    page: 1,
    pageSize,
  })

  return response.items
}

export async function getRecentFailures(pageSize = 5) {
  const response = await apiGet<PagedResponse<FailedDeliveryItem>>(
    '/v1/admin/deliveries/failures',
    {
      page: 1,
      pageSize,
    },
  )

  return response.items
}
