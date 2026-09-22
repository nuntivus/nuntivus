import { getStoredToken } from '../auth/token-storage'

const defaultApiBaseUrl = 'https://localhost:5001'

const apiBaseUrl =
  import.meta.env.VITE_API_BASE_URL?.trim() || defaultApiBaseUrl

type Primitive = string | number | boolean | null

type QueryValue = Primitive | Primitive[]

interface ApiRequestOptions {
  token?: string
  suppressUnauthorizedEvent?: boolean
}

interface ApiMutationOptions extends ApiRequestOptions {
  body?: unknown
  headers?: Record<string, string>
}

export interface ApiResponse<TData> {
  data: TData
  headers: Headers
  status: number
}

function createUrl(path: string, query?: Record<string, QueryValue>) {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  const url = new URL(`${apiBaseUrl}${normalizedPath}`)

  if (query) {
    Object.entries(query).forEach(([key, value]) => {
      if (value === null) {
        return
      }

      if (Array.isArray(value)) {
        value.forEach((entry) => {
          if (entry !== null) {
            url.searchParams.append(key, String(entry))
          }
        })
        return
      }

      url.searchParams.append(key, String(value))
    })
  }

  return url
}

export class ApiError extends Error {
  status: number
  body?: unknown

  constructor(message: string, status: number, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

function redactSensitiveText(input: string): string {
  return input
    .replace(/((?:authorization|token|secret|password|credential|api[_-]?key)\s*[:=]\s*)([^\s,;]+)/gi, '$1[REDACTED]')
    .replace(/("(?:authorization|token|secret|password|credential|api[_-]?key)"\s*:\s*")([^"]+)(")/gi, '$1[REDACTED]$3')
}

function sanitizeApiErrorBody(body: unknown): unknown {
  if (typeof body === 'string') {
    return redactSensitiveText(body)
  }

  if (!body || typeof body !== 'object') {
    return body
  }

  if (Array.isArray(body)) {
    return body.map((entry) => sanitizeApiErrorBody(entry))
  }

  const obj = body as Record<string, unknown>
  const sanitized: Record<string, unknown> = {}

  Object.entries(obj).forEach(([key, value]) => {
    const lower = key.toLowerCase()
    if (
      lower.includes('secret') ||
      lower.includes('password') ||
      lower.includes('token') ||
      lower.includes('credential') ||
      lower.includes('apikey') ||
      lower.includes('api_key') ||
      lower.includes('authorization')
    ) {
      sanitized[key] = '[REDACTED]'
      return
    }

    sanitized[key] = sanitizeApiErrorBody(value)
  })

  return sanitized
}

export function getApiErrorMessage(error: unknown, fallback = 'Request failed') {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  const safeBody = sanitizeApiErrorBody(error.body)

  if (typeof safeBody === 'string' && safeBody.trim()) {
    return safeBody
  }

  if (safeBody && typeof safeBody === 'object') {
    const body = safeBody as {
      message?: string
      detail?: string
      title?: string
      errors?: Array<{ message?: string }>
    }

    if (typeof body.message === 'string' && body.message.trim()) {
      return body.message
    }

    if (Array.isArray(body.errors) && body.errors.length > 0) {
      const first = body.errors.find((item) => item.message?.trim())
      if (first?.message) {
        return first.message
      }
    }

    if (typeof body.detail === 'string' && body.detail.trim()) {
      return body.detail
    }

    if (typeof body.title === 'string' && body.title.trim()) {
      return body.title
    }
  }

  return fallback
}

function publishApiError(message: string) {
  window.dispatchEvent(
    new CustomEvent('kairo:api-error', {
      detail: { message },
    }),
  )
}

function publishUnauthorized() {
  window.dispatchEvent(new CustomEvent('kairo:auth-unauthorized'))
}

async function apiRequest<TResponse>(
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  query?: Record<string, QueryValue>,
  options?: ApiMutationOptions,
): Promise<ApiResponse<TResponse>> {
  const token = options?.token ?? getStoredToken()

  let response: Response

  try {
    response = await fetch(createUrl(path, query), {
      method,
      headers: {
        Accept: 'application/json, text/plain;q=0.9',
        ...(options?.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(options?.headers ?? {}),
      },
      body: options?.body ? JSON.stringify(options.body) : undefined,
    })
  } catch {
    publishApiError('Network error while contacting API')
    throw new ApiError('Network error while contacting API', 0)
  }

  const contentType = response.headers.get('content-type') ?? ''
  const isJson = contentType.includes('application/json')
  const body = isJson ? await response.json() : await response.text()

  if (!response.ok) {
    if (response.status === 401 && !options?.suppressUnauthorizedEvent) {
      publishUnauthorized()
    }

    if (response.status !== 403) {
      publishApiError(`Request failed (${response.status})`)
    }

    throw new ApiError('Request failed', response.status, body)
  }

  return {
    data: body as TResponse,
    headers: response.headers,
    status: response.status,
  }
}

export async function apiGet<TResponse>(
  path: string,
  query?: Record<string, QueryValue>,
  options?: ApiRequestOptions,
): Promise<TResponse> {
  const response = await apiRequest<TResponse>('GET', path, query, options)
  return response.data
}

export async function apiPost<TResponse>(
  path: string,
  body?: unknown,
  options?: ApiMutationOptions,
): Promise<TResponse> {
  const response = await apiRequest<TResponse>('POST', path, undefined, {
    ...options,
    body,
  })
  return response.data
}

export async function apiPut<TResponse>(
  path: string,
  body?: unknown,
  options?: ApiMutationOptions,
): Promise<TResponse> {
  const response = await apiRequest<TResponse>('PUT', path, undefined, {
    ...options,
    body,
  })
  return response.data
}

export async function apiDelete<TResponse>(
  path: string,
  options?: ApiRequestOptions,
): Promise<TResponse> {
  const response = await apiRequest<TResponse>('DELETE', path, undefined, options)
  return response.data
}

export async function apiGetWithResponse<TResponse>(
  path: string,
  query?: Record<string, QueryValue>,
  options?: ApiRequestOptions,
): Promise<ApiResponse<TResponse>> {
  return apiRequest<TResponse>('GET', path, query, options)
}

export async function apiPutWithResponse<TResponse>(
  path: string,
  body?: unknown,
  options?: ApiMutationOptions,
): Promise<ApiResponse<TResponse>> {
  return apiRequest<TResponse>('PUT', path, undefined, {
    ...options,
    body,
  })
}
