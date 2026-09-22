export type SavedViewScope = 'deliveries' | 'events' | 'contracts'

export interface SavedView {
  id: string
  name: string
  scope: SavedViewScope
  query: string
  createdAt: string
  updatedAt: string
}

const keyPrefix = 'kairo.dashboard.saved-views.v1'

function getStorageKey(subject: string, scope: SavedViewScope) {
  return `${keyPrefix}.${subject}.${scope}`
}

function nowIso() {
  return new Date().toISOString()
}

function normalize(input: unknown, scope: SavedViewScope): SavedView[] {
  if (!Array.isArray(input)) {
    return []
  }

  return input
    .map((entry) => {
      if (!entry || typeof entry !== 'object') {
        return null
      }

      const value = entry as Partial<SavedView>
      if (!value.id || !value.name || !value.query) {
        return null
      }

      return {
        id: String(value.id),
        name: String(value.name),
        scope,
        query: String(value.query),
        createdAt: value.createdAt ? String(value.createdAt) : nowIso(),
        updatedAt: value.updatedAt ? String(value.updatedAt) : nowIso(),
      } satisfies SavedView
    })
    .filter((entry): entry is SavedView => entry !== null)
    .sort((a, b) => a.name.localeCompare(b.name))
}

export function getSavedViews(subject: string | null | undefined, scope: SavedViewScope): SavedView[] {
  if (!subject?.trim()) {
    return []
  }

  try {
    const raw = window.localStorage.getItem(getStorageKey(subject.trim(), scope))
    if (!raw) {
      return []
    }

    return normalize(JSON.parse(raw), scope)
  } catch {
    return []
  }
}

function persistSavedViews(subject: string, scope: SavedViewScope, views: SavedView[]) {
  window.localStorage.setItem(getStorageKey(subject, scope), JSON.stringify(views))
}

export function upsertSavedView(
  subject: string,
  scope: SavedViewScope,
  request: { id?: string; name: string; query: string },
): SavedView[] {
  const trimmedSubject = subject.trim()
  const now = nowIso()
  const current = getSavedViews(trimmedSubject, scope)
  const id = request.id ?? `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`

  const nextView: SavedView = {
    id,
    name: request.name.trim(),
    scope,
    query: request.query,
    createdAt: current.find((entry) => entry.id === id)?.createdAt ?? now,
    updatedAt: now,
  }

  const next = [...current.filter((entry) => entry.id !== id), nextView].sort((a, b) =>
    a.name.localeCompare(b.name),
  )

  persistSavedViews(trimmedSubject, scope, next)
  return next
}

export function deleteSavedView(subject: string, scope: SavedViewScope, id: string): SavedView[] {
  const trimmedSubject = subject.trim()
  const next = getSavedViews(trimmedSubject, scope).filter((entry) => entry.id !== id)
  persistSavedViews(trimmedSubject, scope, next)
  return next
}

export function clearSavedViews(subject: string, scope: SavedViewScope) {
  window.localStorage.removeItem(getStorageKey(subject.trim(), scope))
}
