import { apiGet, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export interface AuditLogListItem {
  id: string
  category: string
  action: string
  actorId: string | null
  customerId: string | null
  contractId: string | null
  deliveryId: string | null
  eventId: string | null
  operationId: string | null
  details: string
  createdAt: string
  expiresAt: string
}

export interface AuditLogListQuery {
  category?: string
  action?: string
  actorId?: string
  customerId?: string
  contractId?: string
  deliveryId?: string
  eventId?: string
  operationId?: string
  createdFrom?: string
  createdTo?: string
  sortDirection?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export async function listAuditLogs(query: AuditLogListQuery, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<AuditLogListItem>>('/v1/admin/audit-logs', {
    category: query.category?.trim() ? query.category.trim() : null,
    action: query.action?.trim() ? query.action.trim() : null,
    actorId: query.actorId?.trim() ? query.actorId.trim() : null,
    customerId: query.customerId?.trim() ? query.customerId.trim() : null,
    contractId: query.contractId?.trim() ? query.contractId.trim() : null,
    deliveryId: query.deliveryId?.trim() ? query.deliveryId.trim() : null,
    eventId: query.eventId?.trim() ? query.eventId.trim() : null,
    operationId: query.operationId?.trim() ? query.operationId.trim() : null,
    createdFrom: query.createdFrom ?? null,
    createdTo: query.createdTo ?? null,
    sortDirection: query.sortDirection ?? null,
    page: query.page,
    pageSize: query.pageSize,
  }, options)
}