import { apiGet, apiPost } from './api-client'

export type DeliveryStatus =
  | 'Pending'
  | 'InFlight'
  | 'Delivered'
  | 'Failed'
  | 'Dead'
  | (string & {})

export type DestinationType = 'Http' | 'Kafka' | 'Nats' | 'RabbitMq' | 'Sqs'

export interface PagedResponse<TItem> {
  items: TItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export interface DeliveryAttemptSummary {
  totalAttempts: number
  successfulAttempts: number
  failedAttempts: number
  lastAttemptAt: string | null
}

export interface DeliveryLatestFailure {
  failureCategory: string | null
  error: string | null
  failedAt: string | null
}

export interface DeliveryListItem {
  id: string
  eventId: string
  customerId: string
  contractId: string
  destinationType: DestinationType | string
  eventType: string
  status: DeliveryStatus
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
  attemptSummary: DeliveryAttemptSummary
  latestFailure: DeliveryLatestFailure | null
}

export interface DeliveryListQuery {
  eventId?: string
  customerId?: string
  contractId?: string
  destinationType?: DestinationType
  failureCategory?: string
  status?: DeliveryStatus
  createdFrom?: string
  createdTo?: string
  sortBy?: 'createdAt' | 'updatedAt'
  sortDirection?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export interface DeliveryFailure {
  deliveryId: string
  status: DeliveryStatus
  lastError: string | null
  failureCategory: string | null
  failedAt: string | null
  nextAttemptAt: string | null
  updatedAt: string
}

export interface DeliveryAttempt {
  id: string
  attemptNumber: number
  succeeded: boolean
  failureCategory: string | null
  error: string | null
  metadata: Record<string, string>
  startedAt: string
  completedAt: string
  durationMs: number
}

export interface ManualDeliveryOperationRequest {
  operationId: string
  reason?: string | null
}

export interface DeliveryOperationResponse {
  id: string
  status: DeliveryStatus
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  updatedAt: string
}

export async function listDeliveries(query: DeliveryListQuery) {
  return apiGet<PagedResponse<DeliveryListItem>>('/v1/deliveries', {
    eventId: query.eventId?.trim() ? query.eventId.trim() : null,
    customerId: query.customerId?.trim() ? query.customerId.trim() : null,
    contractId: query.contractId?.trim() ? query.contractId.trim() : null,
    destinationType: query.destinationType ?? null,
    failureCategory: query.failureCategory?.trim() ? query.failureCategory.trim() : null,
    status: query.status ?? null,
    createdFrom: query.createdFrom ?? null,
    createdTo: query.createdTo ?? null,
    sortBy: query.sortBy ?? null,
    sortDirection: query.sortDirection ?? null,
    page: query.page,
    pageSize: query.pageSize,
  })
}

export async function getDeliveryById(id: string) {
  return apiGet<DeliveryListItem>(`/v1/deliveries/${encodeURIComponent(id)}`)
}

export async function getDeliveryFailure(id: string) {
  return apiGet<DeliveryFailure>(`/v1/deliveries/${encodeURIComponent(id)}/failure`)
}

export async function getDeliveryAttempts(id: string) {
  return apiGet<DeliveryAttempt[]>(`/v1/deliveries/${encodeURIComponent(id)}/attempts`)
}

export async function retryDelivery(id: string, request: ManualDeliveryOperationRequest) {
  return apiPost<DeliveryOperationResponse>(`/v1/deliveries/${encodeURIComponent(id)}/retry`, request)
}

export async function requeueDelivery(id: string, request: ManualDeliveryOperationRequest) {
  return apiPost<DeliveryOperationResponse>(`/v1/deliveries/${encodeURIComponent(id)}/requeue`, request)
}
