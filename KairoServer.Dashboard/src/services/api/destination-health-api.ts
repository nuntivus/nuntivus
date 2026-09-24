import { apiGet, type ApiRequestOptions } from './api-client'

export type DestinationHealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy' | number | string

export interface DestinationHealthSnapshot {
  contractId: string
  customerId: string
  destinationType: string
  consecutiveFailures: number
  lastSuccessAt: string | null
  lastFailureAt: string | null
  lastAttemptAt: string
  lastLatencyMs: number | null
  averageLatencyMs: number | null
  latencySampleCount: number
  status: DestinationHealthStatus
  updatedAt: string
}

export async function getDestinationHealthByContractId(
  contractId: string,
  options?: ApiRequestOptions,
) {
  return apiGet<DestinationHealthSnapshot>(
    `/v1/destination-health/contracts/${encodeURIComponent(contractId)}`,
    undefined,
    options,
  )
}

export async function listDestinationHealthByCustomerId(
  customerId: string,
  options?: ApiRequestOptions,
) {
  return apiGet<DestinationHealthSnapshot[]>(
    `/v1/destination-health/customers/${encodeURIComponent(customerId)}`,
    undefined,
    options,
  )
}
