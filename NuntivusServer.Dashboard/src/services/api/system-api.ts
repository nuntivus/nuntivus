import { apiGet, type ApiRequestOptions } from './api-client'

export interface ReadinessStatus {
  status: string
}

export async function getReadinessStatus(options?: ApiRequestOptions): Promise<ReadinessStatus> {
  const response = await apiGet<string | { status?: string }>('/health/ready', undefined, options)

  if (typeof response === 'string') {
    return { status: response }
  }

  return { status: response.status ?? 'unknown' }
}
