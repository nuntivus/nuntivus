import { apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export type EventSchemaState = 'Draft' | 'Active' | 'Deprecated'
export type SchemaCompatibilityMode = 'Backward' | 'Forward'

export interface EventDefinition {
  eventType: string
  displayName: string
  description: string | null
  createdAt: string
}

export interface EventSchemaVersion {
  eventType: string
  version: number
  schema: string
  compatibilityMode: SchemaCompatibilityMode
  state: EventSchemaState
  createdAt: string
  activatedAt: string | null
  deprecatedAt: string | null
}

export interface EventDefinitionListQuery {
  page: number
  pageSize: number
}

export interface CreateEventDefinitionRequest {
  eventType: string
  displayName: string
  description?: string | null
}

export interface UpdateEventDefinitionRequest {
  displayName: string
  description?: string | null
}

export interface CreateSchemaVersionRequest {
  version: number
  schema: string
  compatibilityMode: SchemaCompatibilityMode
}

export interface RelatedContractListItem {
  id: string
  customerId: string
  enabled: boolean
  eventTypes: string[]
  destinationType: string
  createdAt: string
  updatedAt: string
  name?: string | null
}

export async function listEventDefinitions(
  query: EventDefinitionListQuery,
  options?: ApiRequestOptions,
) {
  return apiGet<PagedResponse<EventDefinition>>('/v1/event-definitions', {
    page: query.page,
    pageSize: query.pageSize,
  }, options)
}

export async function getEventDefinition(eventType: string, options?: ApiRequestOptions) {
  return apiGet<EventDefinition>(`/v1/event-definitions/${encodeURIComponent(eventType)}`, undefined, options)
}

export async function createEventDefinition(request: CreateEventDefinitionRequest) {
  return apiPost<EventDefinition>('/v1/event-definitions', {
    eventType: request.eventType,
    displayName: request.displayName,
    description: request.description ?? null,
  })
}

export async function updateEventDefinition(
  eventType: string,
  request: UpdateEventDefinitionRequest,
) {
  return apiPut<EventDefinition>(`/v1/event-definitions/${encodeURIComponent(eventType)}`, {
    displayName: request.displayName,
    description: request.description ?? null,
  })
}

export async function listSchemaVersions(
  eventType: string,
  page = 1,
  pageSize = 20,
  options?: ApiRequestOptions,
) {
  return apiGet<PagedResponse<EventSchemaVersion>>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas`,
    {
      page,
      pageSize,
    },
    options,
  )
}

export async function getSchemaVersion(
  eventType: string,
  version: number,
  options?: ApiRequestOptions,
) {
  return apiGet<EventSchemaVersion>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas/${version}`,
    undefined,
    options,
  )
}

export async function createSchemaDraft(eventType: string, request: CreateSchemaVersionRequest) {
  return apiPost<EventSchemaVersion>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas`,
    {
      version: request.version,
      schema: request.schema,
      compatibilityMode: request.compatibilityMode,
    },
  )
}

export async function activateSchemaVersion(eventType: string, version: number) {
  return apiPost<EventSchemaVersion>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas/${version}/activate`,
  )
}

export async function deprecateSchemaVersion(eventType: string, version: number) {
  return apiPost<EventSchemaVersion>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas/${version}/deprecate`,
  )
}

export async function listContractsRelatedToEventType(
  eventType: string,
  options?: ApiRequestOptions,
) {
  return apiGet<PagedResponse<RelatedContractListItem>>('/v1/admin/contracts', {
    eventType,
    page: 1,
    pageSize: 20,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  }, options)
}
