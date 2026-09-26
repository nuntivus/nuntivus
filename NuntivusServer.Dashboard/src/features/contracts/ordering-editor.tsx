import { Flex, Text } from '@radix-ui/themes'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'

export function OrderingEditor({ value, onChange, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['ordering']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['ordering']>) => void
  errors: ValidationErrors
  disabled?: boolean
}) {
  return <Flex direction="column" gap="2">
    <OptionField label="Ordering key" path="ordering.keySelector" value={value.keySelector} onChange={(keySelector) => onChange({ ...value, keySelector })} errors={errors} disabled={disabled} hint="Dot-separated payload field path, for example customer.id" />
    <Text size="2" color="gray">Events with the same key are processed in order. Leave the key empty for the backend's default ordering behavior.</Text>
  </Flex>
}
