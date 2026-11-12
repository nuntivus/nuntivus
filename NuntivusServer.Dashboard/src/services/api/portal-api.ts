import { apiGet, apiPost } from './api-client'

export const portalSessionKey = 'nuntivus.portal.token'
export function portalGet<T>(path: string, token: string, query?: Record<string, string | number>) {
  return apiGet<T>(`/v1/portal/${path}`, query, { token, suppressUnauthorizedEvent: true })
}
export function portalPost<T>(path: string, body: unknown, token = '') {
  return apiPost<T>(`/v1/portal/${path}`, body, { token, suppressUnauthorizedEvent: true })
}
