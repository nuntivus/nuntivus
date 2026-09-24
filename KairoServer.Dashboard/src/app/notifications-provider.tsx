import { Badge, Button, Card, Flex, Text } from '@radix-ui/themes'
import { useCallback, useMemo, useRef, useState, type PropsWithChildren } from 'react'
import {
  NotificationsContext,
  type NotificationInput,
  type NotificationLevel,
  type NotificationsApi,
} from '../hooks/use-notifications'

interface NotificationItem {
  id: string
  title?: string
  message: string
  level: NotificationLevel
  createdAt: number
}

const MAX_ACTIVE_NOTIFICATIONS = 4
const DEDUPLICATION_WINDOW_MS = 3_000
const DEFAULT_AUTO_CLOSE_MS: Record<NotificationLevel, number> = {
  success: 3_500,
  info: 4_500,
  error: 6_000,
}

function getNotificationColor(level: NotificationLevel): 'green' | 'red' | 'blue' {
  if (level === 'success') {
    return 'green'
  }

  if (level === 'error') {
    return 'red'
  }

  return 'blue'
}

export function NotificationsProvider({ children }: PropsWithChildren) {
  const [items, setItems] = useState<NotificationItem[]>([])
  const lastSeenRef = useRef<Map<string, number>>(new Map())
  const timerRef = useRef<Map<string, number>>(new Map())

  const removeNotification = useCallback((id: string) => {
    setItems((current) => current.filter((item) => item.id !== id))

    const timer = timerRef.current.get(id)
    if (timer) {
      window.clearTimeout(timer)
      timerRef.current.delete(id)
    }
  }, [])

  const notify = useCallback((input: NotificationInput) => {
    const level = input.level ?? 'info'
    const title = input.title?.trim() || undefined
    const message = input.message.trim()

    if (!message) {
      return
    }

    const dedupeKey = `${level}:${title ?? ''}:${message}`
    const now = Date.now()
    const lastSeen = lastSeenRef.current.get(dedupeKey)

    if (lastSeen && now - lastSeen < DEDUPLICATION_WINDOW_MS) {
      return
    }

    lastSeenRef.current.set(dedupeKey, now)

    const id = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `${now}-${Math.random().toString(16).slice(2)}`

    const next: NotificationItem = {
      id,
      title,
      message,
      level,
      createdAt: now,
    }

    setItems((current) => [next, ...current].slice(0, MAX_ACTIVE_NOTIFICATIONS))

    const autoCloseMs = input.autoCloseMs ?? DEFAULT_AUTO_CLOSE_MS[level]
    const timeout = window.setTimeout(() => {
      removeNotification(id)
    }, autoCloseMs)

    timerRef.current.set(id, timeout)
  }, [removeNotification])

  const api = useMemo<NotificationsApi>(() => ({
    notify,
    success: (message, title) => notify({ message, title, level: 'success' }),
    error: (message, title) => notify({ message, title, level: 'error' }),
    info: (message, title) => notify({ message, title, level: 'info' }),
  }), [notify])

  return (
    <NotificationsContext.Provider value={api}>
      {children}
      <Flex
        direction="column"
        gap="2"
        role="region"
        aria-live="polite"
        aria-label="Notifications"
        style={{
          position: 'fixed',
          top: 16,
          right: 16,
          width: 360,
          maxWidth: 'calc(100vw - 32px)',
          zIndex: 9999,
          pointerEvents: 'none',
        }}
      >
        {items.map((item) => (
          <Card key={item.id} size="2" role="status" aria-live="polite" style={{ pointerEvents: 'auto' }}>
            <Flex direction="column" gap="2">
              <Flex justify="between" align="start" gap="2">
                <Flex direction="column" gap="1">
                  <Badge color={getNotificationColor(item.level)} variant="soft">
                    {item.level.toUpperCase()}
                  </Badge>
                  {item.title ? (
                    <Text weight="medium" size="2">{item.title}</Text>
                  ) : null}
                </Flex>
                <Button size="1" variant="ghost" onClick={() => removeNotification(item.id)}>
                  Dismiss
                </Button>
              </Flex>
              <Text size="2">{item.message}</Text>
            </Flex>
          </Card>
        ))}
      </Flex>
    </NotificationsContext.Provider>
  )
}
