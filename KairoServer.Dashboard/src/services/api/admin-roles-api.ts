import { apiGet, type ApiRequestOptions } from './api-client'

export interface AdminRole {
  name: string
  description: string
  permissions: string[]
}

interface AdminRoleCatalogResponse {
  roles: AdminRole[]
}

export async function listAdminRoles(options?: ApiRequestOptions) {
  const response = await apiGet<AdminRoleCatalogResponse>('/v1/admin/roles', undefined, options)
  return response.roles
}
