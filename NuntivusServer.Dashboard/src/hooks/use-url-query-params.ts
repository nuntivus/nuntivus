import { useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'

const sensitiveParamPattern = /(token|secret|password|credential|authorization|api[_-]?key)/i

export function parsePositiveInt(value: string | null, fallback: number) {
  if (!value) {
    return fallback
  }

  const parsed = Number.parseInt(value, 10)
  if (Number.isNaN(parsed) || parsed < 1) {
    return fallback
  }

  return parsed
}

export function parseBooleanParam(value: string | null): boolean | undefined {
  if (value === 'true') {
    return true
  }

  if (value === 'false') {
    return false
  }

  return undefined
}

export function useUrlQueryParams() {
  const [searchParams, setSearchParams] = useSearchParams()

  const updateParams = useCallback(
    (updates: Record<string, string | null>) => {
      const next = new URLSearchParams(searchParams)

      Object.entries(updates).forEach(([key, value]) => {
        if (sensitiveParamPattern.test(key)) {
          next.delete(key)
          return
        }

        if (!value) {
          next.delete(key)
          return
        }

        next.set(key, value)
      })

      setSearchParams(next)
    },
    [searchParams, setSearchParams],
  )

  return {
    searchParams,
    setSearchParams,
    updateParams,
  }
}
