import { useState } from 'react'

type FieldErrors = Record<string, string | null | undefined>

export function useFormValidation<T extends FieldErrors>(errors: T) {
  const [attempted, setAttempted] = useState(false)

  function validate(currentErrors: T = errors) {
    setAttempted(true)
    return !Object.values(currentErrors).some(Boolean)
  }

  return {
    attempted,
    fieldErrors: attempted ? errors : {} as Partial<T>,
    validate,
    reset: () => setAttempted(false),
  }
}
