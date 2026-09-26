import { useEffect } from 'react'
import { loadCurrentUser } from '../store/auth-slice'
import { useAppDispatch, useAppSelector } from '../store/hooks'

export function AuthBootstrap() {
  const dispatch = useAppDispatch()
  const { token, currentUser, status } = useAppSelector((state) => state.auth)

  useEffect(() => {
    if (token && !currentUser && status === 'loading') {
      void dispatch(loadCurrentUser(token))
    }
  }, [currentUser, dispatch, status, token])

  return null
}
