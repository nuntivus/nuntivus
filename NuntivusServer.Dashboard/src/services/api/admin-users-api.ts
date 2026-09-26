import { apiDelete, apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'

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
  password?: string | null
}

export interface ReplaceAdminUserRolesRequest {
  displayName?: string | null
  roles: string[]
  password?: string | null
}

export async function listAdminUsers(options?: ApiRequestOptions) {
  return apiGet<AdminUserRoleAssignment[]>('/v1/admin/users', undefined, options)
}

export async function getAdminUser(subject: string, options?: ApiRequestOptions) {
  return apiGet<AdminUserRoleAssignment>(`/v1/admin/users/${encodeURIComponent(subject)}`, undefined, options)
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
