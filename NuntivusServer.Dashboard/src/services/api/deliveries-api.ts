import { apiGet, apiPost, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export type DeliveryStatusName =
  | 'Pending'
  | 'InFlight'
  | 'Delivered'
  | 'Failed'
  | 'Dead'
  | (string & {})

export type DeliveryStatus = DeliveryStatusName | number

export type DestinationType = 'Http' | 'Kafka' | 'Nats' | 'RabbitMq' | 'Sqs'

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
  destinationId: string | null
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
  search?: string
  eventId?: string
  customerId?: string
  contractId?: string
  destinationType?: DestinationType
  failureCategory?: string
  status?: DeliveryStatusName
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

export async function listDeliveries(query: DeliveryListQuery, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<DeliveryListItem>>('/v1/deliveries', {
    search: query.search?.trim() || null,
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
  }, options)
}

export async function getDeliveryById(id: string, options?: ApiRequestOptions) {
  return apiGet<DeliveryListItem>(`/v1/deliveries/${encodeURIComponent(id)}`, undefined, options)
}

export async function getDeliveryFailure(id: string, options?: ApiRequestOptions) {
  return apiGet<DeliveryFailure>(`/v1/deliveries/${encodeURIComponent(id)}/failure`, undefined, options)
}

export async function getDeliveryAttempts(id: string, options?: ApiRequestOptions) {
  return apiGet<DeliveryAttempt[]>(`/v1/deliveries/${encodeURIComponent(id)}/attempts`, undefined, options)
}

export async function retryDelivery(id: string, request: ManualDeliveryOperationRequest) {
  return apiPost<DeliveryOperationResponse>(`/v1/deliveries/${encodeURIComponent(id)}/retry`, request)
}

export async function requeueDelivery(id: string, request: ManualDeliveryOperationRequest) {
  return apiPost<DeliveryOperationResponse>(`/v1/deliveries/${encodeURIComponent(id)}/requeue`, request)
}
