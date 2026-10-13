import { apiDelete, apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export interface Source {
  id: string
  name: string
  description: string | null
  type: string
  enabled: boolean
  configuration: Record<string, unknown> & { authentication?: { type: 'none' | 'token'; secretRef?: string } }
  createdAt: string
  updatedAt: string
}

export interface SourceInput {
  type?: string
  name: string
  description: string | null
  configuration: Source['configuration']
}

export function listSources(page = 1, pageSize = 100, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<Source>>('/v1/sources', { page, pageSize }, options)
}

export function getSourceById(id: string, options?: ApiRequestOptions) {
  return apiGet<Source>(`/v1/sources/${encodeURIComponent(id)}`, undefined, options)
}

export async function listAllSources(options?: ApiRequestOptions) {
  const items: Source[] = []
  let page = 1
  while (true) {
    const result = await listSources(page, 100, options)
    items.push(...result.items)
    if (!result.hasNext) return items
    page++
  }
}

export function createSource(input: SourceInput) {
  return apiPost<Source>('/v1/sources', { ...input, type: input.type ?? 'HTTP_API' })
}

export function updateSource(id: string, input: SourceInput) {
  return apiPut<Source>(`/v1/sources/${encodeURIComponent(id)}`, input)
}

export function testSourceConnection(input: Pick<SourceInput, 'type' | 'configuration'>, id?: string) {
  return apiPost<{ message: string }>(id
    ? `/v1/sources/${encodeURIComponent(id)}/test-connection` : '/v1/sources/test-connection', input)
}

export function enableSource(id: string) {
  return apiPost<Source>(`/v1/sources/${encodeURIComponent(id)}/enable`)
}

export function disableSource(id: string) {
  return apiPost<Source>(`/v1/sources/${encodeURIComponent(id)}/disable`)
}

export function deleteSource(id: string) {
  return apiDelete<void>(`/v1/sources/${encodeURIComponent(id)}`)
}
