import { QueryClientProvider } from '@tanstack/react-query'
import { Theme } from '@radix-ui/themes'
import { Provider as ReduxProvider } from 'react-redux'
import { BrowserRouter } from 'react-router-dom'
import type { PropsWithChildren } from 'react'
import { AppContextProvider } from '../context/app-context'
import { queryClient } from './queryClient'
import { store } from '../store/store'

export function AppProviders({ children }: PropsWithChildren) {
  return (
    <Theme appearance="light" accentColor="indigo" grayColor="slate" radius="medium">
      <QueryClientProvider client={queryClient}>
        <ReduxProvider store={store}>
          <AppContextProvider>
            <BrowserRouter>{children}</BrowserRouter>
          </AppContextProvider>
        </ReduxProvider>
      </QueryClientProvider>
    </Theme>
  )
}
