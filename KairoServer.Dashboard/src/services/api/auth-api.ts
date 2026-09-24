import { apiGet, apiPost, type ApiRequestOptions } from './api-client'

export interface CurrentUser {
  subject: string
  displayName: string | null
  roles: string[]
  permissions: string[]
}

export interface LocalLoginRequest {
  username: string
  password: string
}

export interface LocalLoginResponse {
  accessToken: string
  tokenType: string
  expiresAt: string
  expiresInSeconds: number
}

export async function loginWithPassword(request: LocalLoginRequest): Promise<LocalLoginResponse> {
  return apiPost<LocalLoginResponse>('/v1/auth/local/login', request, {
    suppressUnauthorizedEvent: true,
  })
}

export async function getCurrentUser(
  token?: string,
  options?: ApiRequestOptions,
): Promise<CurrentUser> {
  return apiGet<CurrentUser>('/v1/admin/users/me', undefined, {
    ...options,
    token,
    suppressUnauthorizedEvent: false,
  })
}
