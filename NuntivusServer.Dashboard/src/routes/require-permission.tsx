import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { hasAnyPermission } from '../auth/permissions'
import { useAppSelector } from '../store/hooks'

interface RequirePermissionProps {
  permissions: string[]
}

export function RequirePermission({ permissions }: RequirePermissionProps) {
  const location = useLocation()
  const { currentUser } = useAppSelector((state) => state.auth)

  if (!hasAnyPermission(currentUser, permissions)) {
    return <Navigate to="/forbidden" replace state={{ from: location }} />
  }

  return <Outlet />
}
