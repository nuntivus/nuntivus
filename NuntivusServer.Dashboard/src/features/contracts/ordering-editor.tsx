import { Flex, Text } from '@radix-ui/themes'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { PayloadFieldPicker } from './filters-editor'
import { useSchemaFieldOptions } from './use-schema-field-options'

export function OrderingEditor({ value, onChange, eventTypes, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['ordering']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['ordering']>) => void
  eventTypes: string[]
  errors: ValidationErrors
  disabled?: boolean
}) {
  const schemaFields = useSchemaFieldOptions(eventTypes)
  return <Flex direction="column" gap="2">
    <PayloadFieldPicker label="Ordering key" path="ordering.keySelector" value={value.keySelector} onChange={(keySelector) => onChange({ ...value, keySelector })} fields={schemaFields.options} hint="Choose a payload field, enter a custom path, or leave empty for default ordering." loading={schemaFields.isLoading} loadError={schemaFields.isError} errors={errors} disabled={disabled} />
    <Text size="2" color="gray">Events with the same key are processed in order. Leave the key empty for the backend's default ordering behavior.</Text>
  </Flex>
}
