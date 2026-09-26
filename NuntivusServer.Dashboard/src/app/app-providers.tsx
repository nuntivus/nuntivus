import { QueryClientProvider } from '@tanstack/react-query'
import { Theme } from '@radix-ui/themes'
import { useMemo } from 'react'
import { Provider as ReduxProvider } from 'react-redux'
import { BrowserRouter } from 'react-router-dom'
import type { PropsWithChildren } from 'react'
import { AuthBootstrap } from './auth-bootstrap'
import { AuthSessionListener } from './auth-session-listener'
import { GlobalStatus } from './global-status'
import { NotificationsProvider } from './notifications-provider'
import { AppContextProvider } from '../context/app-context'
import { useLocalUserPreferences } from '../features/settings/local-user-preferences'
import { ConfirmDialogProvider } from '../hooks/confirm-dialog-provider'
import { queryClient } from './queryClient'
import { useAppSelector } from '../store/hooks'
import { store } from '../store/store'

function ThemeHost({ children }: PropsWithChildren) {
  const subject = useAppSelector((state) => state.auth.currentUser?.subject)
  const { preferences } = useLocalUserPreferences(subject)

  const appearance = useMemo(
    () => (preferences.theme === 'dark' ? 'dark' : 'light'),
    [preferences.theme],
  )

  return (
    <Theme
      appearance={appearance}
      accentColor="indigo"
      grayColor="slate"
      panelBackground="solid"
      radius="medium"
      scaling="95%"
    >
      {children}
    </Theme>
  )
}

export function AppProviders({ children }: PropsWithChildren) {
  return (
    <QueryClientProvider client={queryClient}>
      <ReduxProvider store={store}>
        <ThemeHost>
          <AppContextProvider>
            <BrowserRouter>
              <NotificationsProvider>
                <ConfirmDialogProvider>
                  <AuthBootstrap />
                  <AuthSessionListener />
                  <GlobalStatus />
                  {children}
                </ConfirmDialogProvider>
              </NotificationsProvider>
            </BrowserRouter>
          </AppContextProvider>
        </ThemeHost>
      </ReduxProvider>
    </QueryClientProvider>
  )
}
