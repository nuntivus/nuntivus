import { Flex, Text } from '@radix-ui/themes'
import { FieldErrorText } from '../../components/forms/form-feedback'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'

export function RateLimitEditor({ value, onChange, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['rateLimit']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['rateLimit']>) => void
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="2">
    <Flex gap="3" wrap="wrap">
      <OptionField label="Requests per second" path="rateLimit.perSecond" type="number" min={1} max={1_000_000} value={value.perSecond} onChange={(perSecond) => onChange({ ...value, perSecond })} errors={errors} disabled={disabled} />
      <OptionField label="Requests per minute" path="rateLimit.perMinute" type="number" min={1} max={60_000_000} value={value.perMinute} onChange={(perMinute) => onChange({ ...value, perMinute })} errors={errors} disabled={disabled} />
      <OptionField label="Burst capacity (optional)" path="rateLimit.burst" type="number" min={1} max={10_000_000} value={value.burst} onChange={(burst) => onChange({ ...value, burst })} errors={errors} disabled={disabled} />
    </Flex>
    <FieldErrorText id="rate-limit-error" message={errors.rateLimit} />
    <Text size="1" color="gray">Burst allows a short spike of requests above the steady rate. Set a per-second or per-minute limit (or both).</Text>
  </Flex>
}
