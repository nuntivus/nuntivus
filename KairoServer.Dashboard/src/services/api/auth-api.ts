import { apiGet } from './api-client'

export interface CurrentUser {
  subject: string
  displayName: string | null
  roles: string[]
  permissions: string[]
}

export async function getCurrentUser(token?: string): Promise<CurrentUser> {
  return apiGet<CurrentUser>('/v1/admin/users/me', undefined, {
    token,
    suppressUnauthorizedEvent: false,
  })
}
