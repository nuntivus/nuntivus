import { apiDelete, apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'
import { mapDestinationToApi, type DestinationConfigInput } from './contracts-api'
import type { PagedResponse } from './paged-response'

export interface DestinationResource {
  id: string
  name: string
  description: string | null
  type: string | number
  configuration: string
  enabled: boolean
  configured: boolean
  disablePolicy: 'continue' | 'pause'
  createdAt: string
  updatedAt: string
}

export interface DestinationDraftInput {
  name: string
  description: string | null
  type: 'Http' | 'Kafka' | 'Nats' | 'RabbitMq' | 'Sqs'
}

export function createDestinationDraft(input: DestinationDraftInput) {
  const types = { Http: 1, Kafka: 2, Nats: 3, RabbitMq: 4, Sqs: 5 } as const
  return apiPost<DestinationResource>('/v1/destinations', { ...input, type: types[input.type] })
}

export interface DestinationInput {
  name: string
  description: string | null
  configuration: DestinationConfigInput | Record<string, unknown>
  rawConfiguration?: boolean
}

export function listDestinations(page = 1, pageSize = 100, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<DestinationResource>>('/v1/destinations', { page, pageSize }, options)
}

export async function listAllDestinations(options?: ApiRequestOptions) {
  const items: DestinationResource[] = []
  let page = 1
  while (true) {
    const result = await listDestinations(page, 100, options)
    items.push(...result.items)
    if (!result.hasNext) return items
    page++
  }
}

export function getDestinationById(id: string, options?: ApiRequestOptions) {
  return apiGet<DestinationResource>(`/v1/destinations/${encodeURIComponent(id)}`, undefined, options)
}

export function createDestination(input: DestinationInput) {
  return apiPost<DestinationResource>('/v1/destinations', {
    name: input.name, description: input.description,
    configuration: input.rawConfiguration ? input.configuration : mapDestinationToApi(input.configuration as DestinationConfigInput),
  })
}

export function updateDestination(id: string, input: DestinationInput) {
  return apiPut<DestinationResource>(`/v1/destinations/${encodeURIComponent(id)}`, {
    name: input.name, description: input.description,
    configuration: input.rawConfiguration ? input.configuration : mapDestinationToApi(input.configuration as DestinationConfigInput),
  })
}

export function enableDestination(id: string) {
  return apiPost<DestinationResource>(`/v1/destinations/${encodeURIComponent(id)}/enable`)
}

export function disableDestination(id: string, pauseExistingBindings: boolean) {
  return apiPost<DestinationResource>(`/v1/destinations/${encodeURIComponent(id)}/disable`, { pauseExistingBindings })
}

export function deleteDestination(id: string, removeBindings: boolean) {
  return apiDelete<void>(`/v1/destinations/${encodeURIComponent(id)}?removeBindings=${removeBindings}`)
}
