import { apiPost } from './api-client'
import type { CreateContractRequest } from './contracts-api'

export interface ContractDryRunReport {
  wouldDeliver: boolean
  inputSchemaVersion: number | null
  outputSchemaVersion: number | null
  transformationIdentity: string | null
  transformedPayload: unknown
  stages: { stage: string; status: string; message: string }[]
  destinations: { bindingId: string | null; destinationId: string | null; type: string; enabled: boolean; status: string; message: string }[]
  notice: string
}

export function runContractDryRun(contractId: string | null, request: {
  eventType: string; sourceId: string; payload: Record<string, unknown>; metadata?: Record<string, string>
  draft?: CreateContractRequest; draftEnabled?: boolean
}) {
  return apiPost<ContractDryRunReport>(contractId ? `/v1/contracts/${encodeURIComponent(contractId)}/dry-run` : '/v1/contracts/dry-run', request)
}
