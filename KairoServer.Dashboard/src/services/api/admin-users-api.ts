import { apiDelete, apiGet, apiPost, apiPut } from './api-client'

export interface AdminUserRoleAssignment {
  subject: string
  displayName: string | null
  roles: string[]
  createdAt: string
  updatedAt: string
}

export interface CreateAdminUserRequest {
  subject: string
  displayName?: string | null
  roles: string[]
}

export interface ReplaceAdminUserRolesRequest {
  displayName?: string | null
  roles: string[]
}

export async function listAdminUsers() {
  return apiGet<AdminUserRoleAssignment[]>('/v1/admin/users')
}

export async function getAdminUser(subject: string) {
  return apiGet<AdminUserRoleAssignment>(`/v1/admin/users/${encodeURIComponent(subject)}`)
}

export async function createAdminUser(request: CreateAdminUserRequest) {
  return apiPost<AdminUserRoleAssignment>('/v1/admin/users', request)
}

export async function replaceAdminUserRoles(
  subject: string,
  request: ReplaceAdminUserRolesRequest,
) {
  return apiPut<AdminUserRoleAssignment>(
    `/v1/admin/users/${encodeURIComponent(subject)}/roles`,
    request,
  )
}

export async function deleteAdminUser(subject: string) {
  return apiDelete<string>(`/v1/admin/users/${encodeURIComponent(subject)}`)
}
