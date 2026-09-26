const AUTH_TOKEN_KEY = 'nuntivus.dashboard.auth.token'

let inMemoryToken: string | null = null

function readSessionToken() {
  try {
    return window.sessionStorage.getItem(AUTH_TOKEN_KEY)
  } catch {
    return null
  }
}

function writeSessionToken(token: string | null) {
  try {
    if (token === null) {
      window.sessionStorage.removeItem(AUTH_TOKEN_KEY)
      return
    }

    window.sessionStorage.setItem(AUTH_TOKEN_KEY, token)
  } catch {
  }
}

export function getStoredToken(): string | null {
  const persisted = readSessionToken()
  return persisted ?? inMemoryToken
}

export function persistToken(token: string) {
  inMemoryToken = token
  writeSessionToken(token)
}

export function clearStoredToken() {
  inMemoryToken = null
  writeSessionToken(null)
}
