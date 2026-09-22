import { useMemo, useState, type PropsWithChildren } from 'react'
import { AppContext } from './app-context-definition'

export function AppContextProvider({ children }: PropsWithChildren) {
  const [currentWorkspace, setCurrentWorkspace] = useState('default')

  const value = useMemo(
    () => ({ currentWorkspace, setCurrentWorkspace }),
    [currentWorkspace],
  )

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}
