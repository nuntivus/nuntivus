import { useEffect, useMemo, useState } from 'react'

export type ThemePreference = 'light' | 'dark'

export interface UserPreferences {
  theme: ThemePreference
}

const defaultPreferences: UserPreferences = {
  theme: 'light',
}

const keyPrefix = 'kairo.dashboard.preferences.v1'
const changedEventName = 'kairo:user-preferences-changed'

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

    return normalizePreferences(JSON.parse(raw) as Partial<UserPreferences>)
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
  const [preferences, setPreferences] = useState<UserPreferences>(() =>
    getLocalUserPreferences(normalizedSubject),
  )

  useEffect(() => {
    setPreferences(getLocalUserPreferences(normalizedSubject))
  }, [normalizedSubject])

  useEffect(() => {
    function handleStorage(event: StorageEvent) {
      if (!normalizedSubject || event.key !== getStorageKey(normalizedSubject)) {
        return
      }

      setPreferences(getLocalUserPreferences(normalizedSubject))
    }

    function handlePreferencesChanged(event: Event) {
      if (!normalizedSubject) {
        return
      }

      const detail = (event as CustomEvent<{ subject?: string }>).detail
      if (!detail?.subject || detail.subject !== normalizedSubject) {
        return
      }

      setPreferences(getLocalUserPreferences(normalizedSubject))
    }

    window.addEventListener('storage', handleStorage)
    window.addEventListener(changedEventName, handlePreferencesChanged)

    return () => {
      window.removeEventListener('storage', handleStorage)
      window.removeEventListener(changedEventName, handlePreferencesChanged)
    }
  }, [normalizedSubject])

  const actions = useMemo(
    () => ({
      save: (next: UserPreferences) => {
        if (!normalizedSubject) {
          return defaultPreferences
        }

        const saved = saveLocalUserPreferences(normalizedSubject, next)
        setPreferences(saved)
        return saved
      },
      resetToDefault: () => {
        if (!normalizedSubject) {
          setPreferences(defaultPreferences)
          return defaultPreferences
        }

        const saved = saveLocalUserPreferences(normalizedSubject, defaultPreferences)
        setPreferences(saved)
        return saved
      },
    }),
    [normalizedSubject],
  )

  return {
    preferences,
    ...actions,
  }
}
