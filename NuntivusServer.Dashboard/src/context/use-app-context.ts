import { useContext } from 'react'
import { AppContext } from './app-context-definition'

export function useAppContext() {
  const context = useContext(AppContext)

  if (!context) {
    throw new Error('useAppContext must be used within AppContextProvider')
  }

  return context
}
