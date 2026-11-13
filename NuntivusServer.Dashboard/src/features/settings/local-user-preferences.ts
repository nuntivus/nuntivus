import { useCallback, useMemo, useSyncExternalStore } from 'react'

export type ThemePreference = 'light' | 'dark'

export interface UserPreferences {
  theme: ThemePreference
}

const defaultPreferences: UserPreferences = {
  theme: 'light',
}

const keyPrefix = 'nuntivus.dashboard.preferences.v1'
const changedEventName = 'nuntivus:user-preferences-changed'
const snapshots = new Map<string, { raw: string; value: UserPreferences }>()

function getStorageKey(subject: string) {
  return `${keyPrefix}.${subject}`
}

function normalizePreferences(value: Partial<UserPreferences> | null | undefined): UserPreferences {
  return {
    theme: value?.theme === 'dark' ? 'dark' : 'light',
  }
}

export function getDefaultUserPreferences() {
  return defaultPreferences
}

export function getLocalUserPreferences(subject: string | null | undefined): UserPreferences {
  if (!subject?.trim()) {
    return defaultPreferences
  }

  try {
    const raw = window.localStorage.getItem(getStorageKey(subject.trim()))
    if (!raw) {
      return defaultPreferences
    }

    const cached = snapshots.get(subject.trim())
    if (cached?.raw === raw) {
      return cached.value
    }

    const value = normalizePreferences(JSON.parse(raw) as Partial<UserPreferences>)
    snapshots.set(subject.trim(), { raw, value })
    return value
  } catch {
    return defaultPreferences
  }
}

export function saveLocalUserPreferences(subject: string, preferences: UserPreferences) {
  const normalized = normalizePreferences(preferences)
  window.localStorage.setItem(getStorageKey(subject.trim()), JSON.stringify(normalized))
  window.dispatchEvent(new CustomEvent(changedEventName, { detail: { subject: subject.trim() } }))
  return normalized
}

export function useLocalUserPreferences(subject: string | null | undefined) {
  const normalizedSubject = subject?.trim() || null
  const getSnapshot = useCallback(() => getLocalUserPreferences(normalizedSubject), [normalizedSubject])
  const subscribe = useCallback((notify: () => void) => {
    function handleStorage(event: StorageEvent) {
      if (normalizedSubject && (event.key === null || event.key === getStorageKey(normalizedSubject))) notify()
    }
    function handlePreferencesChanged(event: Event) {
      const detail = (event as CustomEvent<{ subject?: string }>).detail
      if (normalizedSubject && detail?.subject === normalizedSubject) notify()
    }
    window.addEventListener('storage', handleStorage)
    window.addEventListener(changedEventName, handlePreferencesChanged)
    return () => {
      window.removeEventListener('storage', handleStorage)
      window.removeEventListener(changedEventName, handlePreferencesChanged)
    }
  }, [normalizedSubject])
  const preferences = useSyncExternalStore(subscribe, getSnapshot, getDefaultUserPreferences)
  const actions = useMemo(
    () => ({
      save: (next: UserPreferences) => (normalizedSubject ? saveLocalUserPreferences(normalizedSubject, next) : defaultPreferences),
      resetToDefault: () => (normalizedSubject ? saveLocalUserPreferences(normalizedSubject, defaultPreferences) : defaultPreferences),
    }),
    [normalizedSubject],
  )
  return {
    preferences,
    ...actions,
  }
}
