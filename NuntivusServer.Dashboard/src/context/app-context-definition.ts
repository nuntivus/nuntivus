import { createContext } from 'react'

export interface AppContextValue {
  currentWorkspace: string
  setCurrentWorkspace: (workspace: string) => void
}

export const AppContext = createContext<AppContextValue | undefined>(undefined)
