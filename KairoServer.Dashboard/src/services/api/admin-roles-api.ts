import { apiGet } from './api-client'

export interface AdminRole {
  name: string
  description: string
  permissions: string[]
}

interface AdminRoleCatalogResponse {
  roles: AdminRole[]
}

export async function listAdminRoles() {
  const response = await apiGet<AdminRoleCatalogResponse>('/v1/admin/roles')
  return response.roles
}
