import { resolveApiBaseUrl, type RuntimeFrontendConfig } from './runtime-config'

declare global {
  interface Window {
    __NUNTIVUS_CONFIG__?: RuntimeFrontendConfig
  }
}

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
const runtimeConfig = window.__NUNTIVUS_CONFIG__
const runtimeEnvironment = parseRuntimeEnvironment(runtimeConfig?.runtimeEnvironment ?? import.meta.env.VITE_APP_ENV ?? mode)
const version = (import.meta.env.VITE_APP_VERSION?.trim() || __APP_VERSION__).trim() || '0.0.0'
const apiBaseUrl = resolveApiBaseUrl(
  runtimeConfig?.apiBaseUrl,
  import.meta.env.VITE_API_BASE_URL,
  import.meta.env.DEV,
  isProduction,
  window.location.hostname,
)

export const frontendConfig: FrontendConfig = {
  apiBaseUrl,
  runtimeEnvironment,
  mode,
  isProduction,
  version,
  featureFlags: parseFeatureFlags(runtimeConfig?.featureFlags ?? import.meta.env.VITE_FEATURE_FLAGS),
}

export function isFeatureEnabled(flag: string): boolean {
  return frontendConfig.featureFlags.has(flag)
}