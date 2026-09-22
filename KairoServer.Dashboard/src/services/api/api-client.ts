import { getStoredToken } from '../auth/token-storage'

const defaultApiBaseUrl = 'https://localhost:5001'

const apiBaseUrl =
  import.meta.env.VITE_API_BASE_URL?.trim() || defaultApiBaseUrl

type Primitive = string | number | boolean | null

type QueryValue = Primitive | Primitive[]

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

export async function apiGet<TResponse>(
  path: string,
  query?: Record<string, QueryValue>,
): Promise<TResponse> {
  const token = getStoredToken()

  const response = await fetch(createUrl(path, query), {
    method: 'GET',
    headers: {
      Accept: 'application/json, text/plain;q=0.9',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  })

  const contentType = response.headers.get('content-type') ?? ''
  const isJson = contentType.includes('application/json')
  const body = isJson ? await response.json() : await response.text()

  if (!response.ok) {
    throw new ApiError('Request failed', response.status, body)
  }

  return body as TResponse
}
