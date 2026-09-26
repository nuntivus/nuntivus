import { useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { clearSensitiveClientState } from './auth-security'
import { queryClient } from './queryClient'
import { markSessionExpired } from '../store/auth-slice'
import { useAppDispatch } from '../store/hooks'

export function AuthSessionListener() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => {
    function handleUnauthorized() {
      if (location.pathname === '/login') {
        return
      }

      dispatch(markSessionExpired())
      clearSensitiveClientState(queryClient)
      navigate('/login', {
        replace: true,
        state: { from: location },
      })
    }

    window.addEventListener('nuntivus:auth-unauthorized', handleUnauthorized)

    return () => {
      window.removeEventListener('nuntivus:auth-unauthorized', handleUnauthorized)
    }
  }, [dispatch, location, navigate])

  return null
}
