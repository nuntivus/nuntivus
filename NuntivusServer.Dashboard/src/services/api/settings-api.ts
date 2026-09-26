import { apiGet, apiPut, type ApiRequestOptions } from './api-client'

export interface InstanceDeliveryDefaultsSettings {
  requestTimeoutSeconds: number
  maxAttempts: number
  batchLimit: number
}

export interface InstanceRetryDefaultsSettings {
  initialDelaySeconds: number
  maxDelaySeconds: number
  backoffMultiplier: number
}

export interface InstanceSecuritySettings {
  requireHttpsDestinations: boolean
  allowPrivateNetworkDestinations: boolean
  requireDestinationCredentialEncryption: boolean
}

export interface InstanceRetentionSettings {
  eventRetentionDays: number
  deliveryAuditRetentionDays: number
  operationAuditRetentionDays: number
}

export interface InstanceLimitsSettings {
  maxEventPayloadBytes: number
  maxEventMetadataBytes: number
  maxContractsPerCustomer: number
  maxConcurrentReplayJobs: number
}

export interface InstanceSettings {
  deliveryDefaults: InstanceDeliveryDefaultsSettings
  retryDefaults: InstanceRetryDefaultsSettings
  security: InstanceSecuritySettings
  retention: InstanceRetentionSettings
  limits: InstanceLimitsSettings
  updatedAt: string
  updatedBy: string | null
}

export interface UpdateInstanceSettingsRequest {
  deliveryDefaults: InstanceDeliveryDefaultsSettings
  retryDefaults: InstanceRetryDefaultsSettings
  security: InstanceSecuritySettings
  retention: InstanceRetentionSettings
  limits: InstanceLimitsSettings
}

export async function getInstanceSettings(options?: ApiRequestOptions) {
  return apiGet<InstanceSettings>('/v1/admin/settings/instance', undefined, options)
}

export async function updateInstanceSettings(request: UpdateInstanceSettingsRequest) {
  return apiPut<InstanceSettings>('/v1/admin/settings/instance', request)
}
