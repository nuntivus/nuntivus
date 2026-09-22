import { Spinner, Text } from '@radix-ui/themes'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAppSelector } from '../store/hooks'

export function RequireAuth() {
  const { token, status } = useAppSelector((state) => state.auth)
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div style={{ minHeight: '100dvh', display: 'grid', placeItems: 'center' }}>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <Spinner size="2" />
          <Text>Restoring session...</Text>
        </div>
      </div>
    )
  }

  if (!token || status !== 'authenticated') {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}
