import type { CurrentUser } from '../services/api/auth-api'

export function hasPermission(
  user: CurrentUser | null | undefined,
  permission: string,
) {
  if (!user) {
    return false
  }

  return user.permissions.some(
    (value) => value.toLowerCase() === permission.toLowerCase(),
  )
}

export function hasAnyPermission(
  user: CurrentUser | null | undefined,
  permissions: string[],
) {
  return permissions.some((permission) => hasPermission(user, permission))
}
