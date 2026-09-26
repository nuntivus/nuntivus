type RuntimeEnvironment = 'development' | 'staging' | 'production' | 'test' | (string & {})

type FrontendConfig = {
  apiBaseUrl: string
  runtimeEnvironment: RuntimeEnvironment
  mode: string
  isProduction: boolean
  version: string
  featureFlags: ReadonlySet<string>
}

function parseRuntimeEnvironment(value: string): RuntimeEnvironment {
  const normalized = value.trim().toLowerCase()
  return normalized || 'development'
}

function parseApiBaseUrl(value: string): string {
  const parsed = new URL(value)
  return parsed.origin
}

function parseFeatureFlags(value: string | undefined): ReadonlySet<string> {
  if (!value) {
    return new Set<string>()
  }

  const flags = value
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)

  return new Set<string>(flags)
}

const mode = import.meta.env.MODE
const isProduction = import.meta.env.PROD
const runtimeEnvironment = parseRuntimeEnvironment(import.meta.env.VITE_APP_ENV ?? mode)
const version = (import.meta.env.VITE_APP_VERSION?.trim() || __APP_VERSION__).trim() || '0.0.0'

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const fallbackApiBaseUrl = import.meta.env.DEV ? 'https://localhost:7123' : undefined
const rawApiBaseUrl = configuredApiBaseUrl || fallbackApiBaseUrl

if (!rawApiBaseUrl) {
  throw new Error('API base URL is required. Set VITE_API_BASE_URL.')
}

const apiBaseUrl = parseApiBaseUrl(rawApiBaseUrl)

if (isProduction) {
  const host = new URL(apiBaseUrl).hostname.toLowerCase()
  if (host === 'localhost' || host === '127.0.0.1' || host === '::1') {
    throw new Error('Production API base URL must not target localhost.')
  }
}

export const frontendConfig: FrontendConfig = {
  apiBaseUrl,
  runtimeEnvironment,
  mode,
  isProduction,
  version,
  featureFlags: parseFeatureFlags(import.meta.env.VITE_FEATURE_FLAGS),
}

export function isFeatureEnabled(flag: string): boolean {
  return frontendConfig.featureFlags.has(flag)
}