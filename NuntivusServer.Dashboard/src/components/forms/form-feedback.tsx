import { Text } from '@radix-ui/themes'

export interface ValidationResult {
  formError: string | null
  fieldErrors: Record<string, string>
}

interface FormErrorTextProps {
  message: string | null
  id?: string
}

export function FormErrorText({ message, id }: FormErrorTextProps) {
  if (!message) {
    return null
  }

  return (
    <Text id={id} size="2" color="red" role="alert" aria-live="polite">
      {message}
    </Text>
  )
}

interface FieldErrorTextProps {
  message: string | null | undefined
  id: string
}

export function FieldErrorText({ message, id }: FieldErrorTextProps) {
  if (!message) {
    return null
  }

  return (
    <Text id={id} size="2" color="red" role="alert" aria-live="polite">
      {message}
    </Text>
  )
}
