import { apiGet, apiPost, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export interface DeliveryListItem {
  id: string
  eventId: string
  customerId: string
  contractId: string
  eventType: string
  status: number | string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export interface EventListItem {
  id: string
  idempotencyKey: string
  type: string
  source: string
  occurredAt: string
  receivedAt: string
  payload: unknown
  metadata: unknown | null
  deliveries: DeliveryListItem[]
}

export interface EventListQuery {
  type?: string
  source?: string
  occurredFrom?: string
  occurredTo?: string
  receivedFrom?: string
  receivedTo?: string
  sortBy?: 'type' | 'source' | 'occurredAt' | 'receivedAt'
  sortDirection?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export interface ReplayEventRequest {
  replayKey: string
  reason?: string | null
}

export interface ReplayEventResponse {
  sourceEventId: string
  replayedEventId: string
  status: string
}

export interface ReplayHistoryItem {
  sourceEventId: string
  replayedEventId: string
  status: string
}

export async function listEvents(query: EventListQuery, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<EventListItem>>('/v1/events', {
    type: query.type?.trim() ? query.type.trim() : null,
    source: query.source?.trim() ? query.source.trim() : null,
    occurredFrom: query.occurredFrom ?? null,
    occurredTo: query.occurredTo ?? null,
    receivedFrom: query.receivedFrom ?? null,
    receivedTo: query.receivedTo ?? null,
    sortBy: query.sortBy ?? null,
    sortDirection: query.sortDirection ?? null,
    page: query.page,
    pageSize: query.pageSize,
  }, options)
}

export async function getEventById(id: string, options?: ApiRequestOptions) {
  return apiGet<EventListItem>(`/v1/events/${encodeURIComponent(id)}`, undefined, options)
}

export async function replayEvent(id: string, request: ReplayEventRequest) {
  return apiPost<ReplayEventResponse>(`/v1/events/${encodeURIComponent(id)}/replay`, {
    replayKey: request.replayKey,
    reason: request.reason?.trim() || null,
  })
}
