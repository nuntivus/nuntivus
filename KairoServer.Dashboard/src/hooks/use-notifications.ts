import { createContext, useContext } from 'react'

export type NotificationLevel = 'success' | 'error' | 'info'

export interface NotificationInput {
  title?: string
  message: string
  level?: NotificationLevel
  autoCloseMs?: number
}

export type NotifyHandler = (input: NotificationInput) => void

export interface NotificationsApi {
  notify: NotifyHandler
  success: (message: string, title?: string) => void
  error: (message: string, title?: string) => void
  info: (message: string, title?: string) => void
}

export const NotificationsContext = createContext<NotificationsApi | null>(null)

export function useNotifications() {
  const context = useContext(NotificationsContext)

  if (!context) {
    throw new Error('useNotifications must be used inside NotificationsProvider')
  }

  return context
}
