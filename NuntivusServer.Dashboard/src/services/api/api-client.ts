import { getStoredToken } from '../auth/token-storage'
import { frontendConfig } from '../../app/frontend-config'

const apiBaseUrl = frontendConfig.apiBaseUrl

type Primitive = string | number | boolean | null

type QueryValue = Primitive | Primitive[]

export interface ApiRequestOptions {
  token?: string
  suppressUnauthorizedEvent?: boolean
  signal?: AbortSignal
}

export interface ApiMutationOptions extends ApiRequestOptions {
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
  headers?: Headers

  constructor(message: string, status: number, body?: unknown, headers?: Headers) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
    this.headers = headers
  }
}

type ProblemDetailsBody = {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Array<{ field?: string; message?: string }> | Record<string, string[] | string>
  message?: string
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

  if (error.status === 0) {
    return 'Network error. Check your connection and try again.'
  }

  const safeBody = sanitizeApiErrorBody(error.body)

  if (typeof safeBody === 'string' && safeBody.trim()) {
    return safeBody
  }

  if (safeBody && typeof safeBody === 'object') {
    const body = safeBody as ProblemDetailsBody

    if (Array.isArray(body.errors) && body.errors.length > 0) {
      const first = body.errors.find((item) => item.message?.trim())
      if (first?.message) {
        return first.message
      }
    }

    if (body.errors && !Array.isArray(body.errors) && typeof body.errors === 'object') {
      const firstFieldError = Object.values(body.errors).find((entry) => {
        if (Array.isArray(entry)) {
          return entry.some((value) => value?.trim())
        }

        return typeof entry === 'string' && entry.trim()
      })

      if (Array.isArray(firstFieldError)) {
        const firstMessage = firstFieldError.find((value) => value?.trim())
        if (firstMessage) {
          return firstMessage
        }
      }

      if (typeof firstFieldError === 'string' && firstFieldError.trim()) {
        return firstFieldError
      }
    }

    if (typeof body.message === 'string' && body.message.trim()) {
      return body.message
    }

    if (typeof body.detail === 'string' && body.detail.trim()) {
      return body.detail
    }

    if (typeof body.title === 'string' && body.title.trim()) {
      return body.title
    }
  }

  switch (error.status) {
    case 400:
      return 'The request is invalid. Check your input and try again.'
    case 401:
      return 'Your session expired. Sign in again and retry.'
    case 403:
      return 'You do not have permission to perform this action.'
    case 404:
      return 'The requested resource was not found.'
    case 409:
      return 'The request could not be completed due to a conflict. Refresh and try again.'
    case 429: {
      const retryAfter = error.headers?.get('Retry-After')
      return retryAfter
        ? `Too many requests. Try again in about ${retryAfter} seconds.`
        : 'Too many requests. Please wait and try again.'
    }
    case 500:
      return 'The server encountered an error. Please try again shortly.'
    default:
      return error.status >= 500
        ? 'Unexpected server error. Please try again shortly.'
        : fallback
  }
}

function publishApiError(message: string) {
  window.dispatchEvent(
    new CustomEvent('nuntivus:api-error', {
      detail: { message },
    }),
  )
}

function publishUnauthorized() {
  window.dispatchEvent(new CustomEvent('nuntivus:auth-unauthorized'))
}

async function readResponseBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.status === 205) {
    return null
  }

  const contentType = response.headers.get('content-type') ?? ''
  const isJson =
    contentType.includes('application/json') ||
    contentType.includes('application/problem+json') ||
    contentType.includes('+json')

  if (!isJson) {
    return await response.text()
  }

  const text = await response.text()
  if (!text.trim()) {
    return null
  }

  try {
    return JSON.parse(text)
  } catch {
    return text
  }
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
      signal: options?.signal,
      headers: {
        Accept: 'application/json, text/plain;q=0.9',
        ...(options?.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(options?.headers ?? {}),
      },
      body: options?.body ? JSON.stringify(options.body) : undefined,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }

    const apiError = new ApiError('Network error while contacting API', 0)
    publishApiError(getApiErrorMessage(apiError, 'Network error while contacting API'))
    throw apiError
  }

  const body = await readResponseBody(response)

  if (!response.ok) {
    const apiError = new ApiError('Request failed', response.status, body, response.headers)

    if (response.status === 401 && !options?.suppressUnauthorizedEvent) {
      publishUnauthorized()
    }

    if (response.status !== 401) {
      publishApiError(getApiErrorMessage(apiError, `Request failed (${response.status})`))
    }

    throw apiError
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
