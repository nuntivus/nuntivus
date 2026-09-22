import { apiGet } from './api-client'

export interface ReadinessStatus {
  status: string
}

export async function getReadinessStatus(): Promise<ReadinessStatus> {
  const response = await apiGet<string | { status?: string }>('/health/ready')

  if (typeof response === 'string') {
    return { status: response }
  }

  return { status: response.status ?? 'unknown' }
}
