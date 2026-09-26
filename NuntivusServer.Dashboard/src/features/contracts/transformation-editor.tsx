import { Button, Card, Flex, Text } from '@radix-ui/themes'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'
import { FieldErrorText } from '../../components/forms/form-feedback'

export function PayloadTransformationEditor({ value, onChange, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['transformation']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['transformation']>) => void
  errors: ValidationErrors
  disabled?: boolean
}) {
  const updateMapping = (index: number, mapping: typeof value.fieldMappings[number]) => onChange({ ...value, fieldMappings: value.fieldMappings.map((item, i) => i === index ? mapping : item) })
  return <Flex direction="column" gap="3">
    <OptionField label="Transformation version" path="transformation.version" value={value.version} onChange={(version) => onChange({ ...value, version })} type="number" min={1} errors={errors} disabled={disabled} hint="Increment when changing how event payloads are transformed." />
    <Text size="2" weight="bold">Field mappings</Text>
    {value.fieldMappings.map((mapping, index) => <Card key={index}>
      <Flex direction="column" gap="2">
        <Flex gap="2" align="center" wrap="wrap">
          <OptionField label={`Source field ${index + 1}`} path={`transformation.fieldMappings.${index}.sourceSelector`} value={mapping.sourceSelector} onChange={(sourceSelector) => updateMapping(index, { ...mapping, sourceSelector })} errors={errors} disabled={disabled} />
          <Text aria-hidden="true">→</Text>
          <OptionField label={`Target field ${index + 1}`} path={`transformation.fieldMappings.${index}.targetSelector`} value={mapping.targetSelector} onChange={(targetSelector) => updateMapping(index, { ...mapping, targetSelector })} errors={errors} disabled={disabled} />
        </Flex>
        <Button type="button" variant="soft" color="red" size="1" disabled={disabled} onClick={() => onChange({ ...value, fieldMappings: value.fieldMappings.filter((_, i) => i !== index) })} aria-label={`Remove field mapping ${index + 1}`} style={{ alignSelf: 'flex-end' }}>Remove mapping</Button>
      </Flex>
    </Card>)}
    <FieldErrorText id="field-mappings-error" message={errors['transformation.fieldMappings']} />
    <Button type="button" variant="soft" disabled={disabled || value.fieldMappings.length >= 100} onClick={() => onChange({ ...value, fieldMappings: [...value.fieldMappings, { sourceSelector: '', targetSelector: '' }] })} style={{ alignSelf: 'flex-start' }}>+ Add field mapping</Button>
  </Flex>
}
