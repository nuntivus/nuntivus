export type RuntimeFrontendConfig = {
  apiBaseUrl?: string
  runtimeEnvironment?: string
  featureFlags?: string
}

function isLoopback(hostname: string): boolean {
  return ['localhost', '127.0.0.1', '[::1]', '::1'].includes(hostname.toLowerCase())
}

export function resolveApiBaseUrl(
  runtimeValue: string | undefined,
  buildValue: string | undefined,
  isDevelopment: boolean,
  isProduction: boolean,
  pageHostname: string,
): string {
  const value = runtimeValue?.trim() || buildValue?.trim() || (isDevelopment ? 'https://localhost:7123' : undefined)
  if (!value) {
    throw new Error('API base URL is required. Set NUNTIVUS_API_BASE_URL for Docker or VITE_API_BASE_URL for Vite.')
  }

  const parsed = new URL(value)
  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password) {
    throw new Error('API base URL must be an HTTP(S) URL without credentials.')
  }
  if (parsed.pathname !== '/' || parsed.search || parsed.hash) {
    throw new Error('API base URL must be an origin without a path, query, or fragment.')
  }
  if (isProduction && isLoopback(parsed.hostname) && !isLoopback(pageHostname)) {
    throw new Error('A remotely hosted Dashboard must not target a localhost API.')
  }

  return parsed.origin
}
