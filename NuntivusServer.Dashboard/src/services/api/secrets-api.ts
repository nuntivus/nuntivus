import { apiDelete, apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'

export type ManagedSecret = {
  name: string
  createdAt: string
  currentVersion: number
}

export type ManagedSecretVersion = {
  version: number
  createdAt: string
}

export function listSecrets(options?: ApiRequestOptions) {
  return apiGet<ManagedSecret[]>('/v1/admin/secrets', undefined, options)
}

export function createSecret(name: string, value: string) {
  return apiPost<{ name: string; currentVersion: number }>('/v1/admin/secrets', { name, value })
}

export function listSecretVersions(name: string, options?: ApiRequestOptions) {
  return apiGet<ManagedSecretVersion[]>(`/v1/admin/secrets/${encodeURIComponent(name)}/versions`, undefined, options)
}

export function updateSecret(name: string, value: string) {
  return apiPut<{ name: string; currentVersion: number }>(`/v1/admin/secrets/${encodeURIComponent(name)}`, { value })
}

export function deleteSecret(name: string) {
  return apiDelete<void>(`/v1/admin/secrets/${encodeURIComponent(name)}`)
}
