import { Flex, Text, TextField } from '@radix-ui/themes'
import { FieldErrorText } from '../../components/forms/form-feedback'
import type { ValidationErrors } from './contract-options'

export function OptionField({ label, path, value, onChange, errors, type, min, max, list, disabled, hint }: {
  label: string
  path: string
  value: string
  onChange: (value: string) => void
  errors: ValidationErrors
  type?: 'number' | 'text'
  min?: number
  max?: number
  list?: string
  disabled?: boolean
  hint?: string
}) {
  const errorId = `${path.replace(/[^a-z0-9]/gi, '-')}-error`
  return <Flex direction="column" gap="1">
    <Text as="label" size="2" weight="medium">{label}
      <TextField.Root type={type} min={min} max={max} step={type === 'number' ? 1 : undefined} list={list} value={value} onChange={(event) => onChange(event.target.value)} disabled={disabled} aria-invalid={!!errors[path]} aria-describedby={errors[path] ? errorId : undefined} />
    </Text>
    {hint ? <Text size="1" color="gray">{hint}</Text> : null}
    <FieldErrorText id={errorId} message={errors[path]} />
  </Flex>
}
