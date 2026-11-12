import { Button, Card, Flex, Text, TextArea } from '@radix-ui/themes'
import { useState } from 'react'
import type { AdvancedOptionsDraft, ValidationErrors } from './contract-options'
import { OptionField } from './option-fields'
import { FieldErrorText } from '../../components/forms/form-feedback'
import { PayloadFieldPicker } from './filters-editor'
import { useSchemaFieldOptions } from './use-schema-field-options'

export function PayloadTransformationEditor({ value, onChange, eventTypes, errors, disabled }: {
  value: NonNullable<AdvancedOptionsDraft['transformation']>
  onChange: (next: NonNullable<AdvancedOptionsDraft['transformation']>) => void
  eventTypes: string[]
  errors: ValidationErrors
  disabled?: boolean
}) {
  const schemaFields = useSchemaFieldOptions(eventTypes)
  const [expressionText, setExpressionText] = useState<string | null>(null)
  const [expressionError, setExpressionError] = useState<string | null>(null)
  const updateMapping = (index: number, mapping: typeof value.fieldMappings[number]) => onChange({ ...value, fieldMappings: value.fieldMappings.map((item, i) => i === index ? mapping : item) })
  return <Flex direction="column" gap="3">
    <OptionField label="Transformation version" path="transformation.version" value={value.version} onChange={(version) => {
      setExpressionText(null)
      setExpressionError(null)
      onChange({ ...value, version, fieldMappings: [], expression: version === '2' ? { op: 'select', path: '' } : undefined })
    }} type="number" min={1} errors={errors} disabled={disabled} hint="Operation format: 1 for field mappings, 2 for typed JSON operations. Content identity changes automatically when configuration changes." />
    {value.version === '2' ? <Flex direction="column" gap="2">
      <Text as="label" htmlFor="transformation-expression" weight="medium">Typed JSON expression</Text>
      <TextArea id="transformation-expression" rows={12} value={expressionText ?? JSON.stringify(value.expression, null, 2)} disabled={disabled}
        aria-describedby="transformation-expression-help transformation-expression-error"
        aria-invalid={!!expressionError || !!errors['transformation.expression']}
        onChange={(event) => {
          const text = event.target.value
          setExpressionText(text)
          try {
            const expression: unknown = JSON.parse(text)
            setExpressionError(null)
            onChange({ ...value, expression })
          } catch {
            setExpressionError('Enter valid JSON before saving.')
            onChange({ ...value, expression: null })
          }
        }} />
      <Text id="transformation-expression-help" size="1" color="gray">Operations: select, constant, default, if, map, object, array, concat, add, equals, and, not. Select uses JSON pointers and root/item scope. Defaults apply only to missing fields. Conditions require booleans. Limits: 32 levels, 1,000 expression nodes, 10,000 execution steps, 1,000,000 characters.</Text>
      <FieldErrorText id="transformation-expression-error" message={expressionError ?? errors['transformation.expression']} />
    </Flex> : <>
    <Text size="2" weight="bold">Field mappings</Text>
    {value.fieldMappings.map((mapping, index) => <Card key={index}>
      <Flex direction="column" gap="2">
        <Flex gap="2" align="center" wrap="wrap">
          <PayloadFieldPicker label={`Source field ${index + 1}`} path={`transformation.fieldMappings.${index}.sourceSelector`} value={mapping.sourceSelector} onChange={(sourceSelector) => updateMapping(index, { ...mapping, sourceSelector })} fields={schemaFields.options} hint="Choose an input schema field or enter a custom path." loading={schemaFields.isLoading} loadError={schemaFields.isError} errors={errors} disabled={disabled} />
          <Text aria-hidden="true">→</Text>
          <OptionField label={`Target field ${index + 1}`} path={`transformation.fieldMappings.${index}.targetSelector`} value={mapping.targetSelector} onChange={(targetSelector) => updateMapping(index, { ...mapping, targetSelector })} errors={errors} disabled={disabled} />
        </Flex>
        <Button type="button" variant="soft" color="red" size="1" disabled={disabled} onClick={() => onChange({ ...value, fieldMappings: value.fieldMappings.filter((_, i) => i !== index) })} aria-label={`Remove field mapping ${index + 1}`} style={{ alignSelf: 'flex-end' }}>Remove mapping</Button>
      </Flex>
    </Card>)}
    <FieldErrorText id="field-mappings-error" message={errors['transformation.fieldMappings']} />
    <Button type="button" variant="soft" disabled={disabled || value.fieldMappings.length >= 100} onClick={() => onChange({ ...value, fieldMappings: [...value.fieldMappings, { sourceSelector: '', targetSelector: '' }] })} style={{ alignSelf: 'flex-start' }}>+ Add field mapping</Button>
    </>}
  </Flex>
}
