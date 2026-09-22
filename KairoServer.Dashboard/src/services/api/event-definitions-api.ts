import { apiGet, apiPost, apiPut } from './api-client'

export type EventSchemaState = 'Draft' | 'Active' | 'Deprecated'
export type SchemaCompatibilityMode = 'Backward' | 'Forward'

export interface PagedResponse<TItem> {
  items: TItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

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

export interface UpdateEventDefinitionDescriptionRequest {
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

export async function listEventDefinitions(query: EventDefinitionListQuery) {
  return apiGet<PagedResponse<EventDefinition>>('/v1/event-definitions', {
    page: query.page,
    pageSize: query.pageSize,
  })
}

export async function getEventDefinition(eventType: string) {
  return apiGet<EventDefinition>(`/v1/event-definitions/${encodeURIComponent(eventType)}`)
}

export async function createEventDefinition(request: CreateEventDefinitionRequest) {
  return apiPost<EventDefinition>('/v1/event-definitions', {
    eventType: request.eventType,
    displayName: request.displayName,
    description: request.description ?? null,
  })
}

export async function updateEventDefinitionDescription(
  eventType: string,
  request: UpdateEventDefinitionDescriptionRequest,
) {
  return apiPut<EventDefinition>(`/v1/event-definitions/${encodeURIComponent(eventType)}`, {
    description: request.description ?? null,
  })
}

export async function listSchemaVersions(eventType: string, page = 1, pageSize = 20) {
  return apiGet<PagedResponse<EventSchemaVersion>>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas`,
    {
      page,
      pageSize,
    },
  )
}

export async function getSchemaVersion(eventType: string, version: number) {
  return apiGet<EventSchemaVersion>(
    `/v1/event-definitions/${encodeURIComponent(eventType)}/schemas/${version}`,
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

export async function listContractsRelatedToEventType(eventType: string) {
  return apiGet<PagedResponse<RelatedContractListItem>>('/v1/admin/contracts', {
    eventType,
    page: 1,
    pageSize: 20,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  })
}
