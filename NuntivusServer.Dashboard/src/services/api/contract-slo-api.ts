import { apiGet, apiPut } from './api-client'

export interface ContractSloPolicy {
  successTarget: number
  latencyTarget: number
  latencyThresholdSeconds: number
  successDeadlineSeconds: number
}
export interface ContractSloBudget {
  eligible: number
  good: number
  bad: number
  pending: number
  attainmentPercent: number | null
  allowedFailures: number
  remainingFailures: number
  budgetRemainingPercent: number | null
}
export interface ContractSloReport {
  policy: ContractSloPolicy
  monitoringStartedAt: string
  windowStart: string
  windowEnd: string
  fullWindowCovered: boolean
  success: ContractSloBudget
  latency: ContractSloBudget
  excluded: number
  notice: string
}
export const getContractSlo = (id: string) => apiGet<ContractSloReport>(`/v1/contracts/${encodeURIComponent(id)}/slo`)
export const configureContractSlo = (id: string, policy: ContractSloPolicy) => apiPut<ContractSloReport>(`/v1/contracts/${encodeURIComponent(id)}/slo`, policy)
