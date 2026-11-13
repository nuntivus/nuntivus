import type { QueryClient } from '@tanstack/react-query'
import { store } from '../store/store'
import { clearGlobalError } from '../store/app-shell-slice'
import { setSidebarOpen } from '../store/ui-slice'
import { clearStoredToken } from '../services/auth/token-storage'

export function clearSensitiveClientState(queryClient: QueryClient) {
  queryClient.clear()
  store.dispatch(clearGlobalError())
  store.dispatch(setSidebarOpen(false))

  clearStoredToken()
}