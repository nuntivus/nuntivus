import { ApiError, getApiErrorMessage } from '../../services/api/api-client'
import type { ValidationResult } from './form-feedback'

type ValidationBody = {
  message?: string
  detail?: string
  title?: string
  errors?:
    | Array<{ field?: string; message?: string }>
    | Record<string, string[] | string>
}

function toFieldMap(errors: ValidationBody['errors']): Record<string, string> {
  if (!errors) {
    return {}
  }

  if (Array.isArray(errors)) {
    return errors.reduce<Record<string, string>>((acc, item, index) => {
      const message = item.message?.trim()
      if (!message) {
        return acc
      }

      const key = item.field?.trim() || `__form_${index}`
      acc[key] = message
      return acc
    }, {})
  }

  return Object.entries(errors).reduce<Record<string, string>>((acc, [field, message]) => {
    if (Array.isArray(message)) {
      const first = message.find((entry) => entry?.trim())
      if (first) {
        acc[field] = first
      }

      return acc
    }

    if (typeof message === 'string' && message.trim()) {
      acc[field] = message
    }

    return acc
  }, {})
}

export function getServerValidation(error: unknown, fallback: string): ValidationResult {
  if (!(error instanceof ApiError) || !error.body || typeof error.body !== 'object') {
    return {
      formError: getApiErrorMessage(error, fallback),
      fieldErrors: {},
    }
  }

  const body = error.body as ValidationBody
  const fieldErrors = toFieldMap(body.errors)

  return {
    formError: getApiErrorMessage(error, fallback),
    fieldErrors,
  }
}

export function getFieldError(fieldErrors: Record<string, string>, ...keys: string[]) {
  for (const key of keys) {
    if (fieldErrors[key]) {
      return fieldErrors[key]
    }
  }

  return null
}
