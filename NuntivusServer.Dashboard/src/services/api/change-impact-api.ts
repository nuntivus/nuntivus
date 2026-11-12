import { apiGet, apiPost, type ApiRequestOptions } from './api-client'
import type { ContractTransformation } from './contracts-api'

export interface ChangeImpactReport {
  eventType: string
  proposedSchemaVersion: number | null
  proposedTransformationIdentity: string | null
  page: number
  pageSize: number
  totalContracts: number
  evidenceNotice: string
  contracts: {
    contractId: string
    customerId: string
    name: string | null
    enabled: boolean
    status: 'Compatible' | 'Breaking' | 'ReviewRequired'
    reason: string
    bindings: { id: string; destinationId: string | null; destinationType: string; deliveryEnabled: boolean }[]
    samples: { eventId: string; accepted: boolean; findings: string[] }[]
  }[]
}

export function getSchemaImpact(eventType: string, version: number, page: number, options?: ApiRequestOptions) {
  return apiGet<ChangeImpactReport>(`/v1/event-definitions/${encodeURIComponent(eventType)}/schemas/${version}/impact`,
    { page, pageSize: 20, sampleSize: 10 }, options)
}

export function getTransformationImpact(contractId: string, eventType: string, transformation: ContractTransformation | null) {
  return apiPost<ChangeImpactReport>(`/v1/contracts/${encodeURIComponent(contractId)}/transformation-impact`,
    { eventType, transformation, sampleSize: 10 })
}
