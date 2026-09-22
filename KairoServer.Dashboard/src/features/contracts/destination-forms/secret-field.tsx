import { Button, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { useMemo } from 'react'
import type { SecretMode } from './types'

type SecretFieldValue = {
  mode: SecretMode
  hasStoredValue: boolean
  kind: 'inline' | 'ref'
  value: string
  version: string
}

type SecretFieldProps = {
  label: string
  value: SecretFieldValue
  onChange: (next: SecretFieldValue) => void
}

export function SecretField({ label, value, onChange }: SecretFieldProps) {
  const modeOptions = useMemo(() => {
    const options: Array<{ value: SecretMode; label: string }> = [
      { value: 'replace', label: value.hasStoredValue ? 'Replace value' : 'Set value' },
    ]

    if (value.hasStoredValue) {
      options.unshift({ value: 'keep', label: 'Keep stored value' })
      options.push({ value: 'remove', label: 'Remove value' })
    }

    return options
  }, [value.hasStoredValue])

  const canShowEditor = value.mode === 'replace'

  return (
    <Flex direction="column" gap="2">
      <Text size="2" color="gray">{label}</Text>

      <Select.Root
        value={value.mode}
        onValueChange={(next) =>
          onChange({
            ...value,
            mode: next as SecretMode,
            value: next === 'replace' ? value.value : '',
            version: next === 'replace' ? value.version : '',
          })
        }
      >
        <Select.Trigger />
        <Select.Content>
          {modeOptions.map((option) => (
            <Select.Item key={option.value} value={option.value}>
              {option.label}
            </Select.Item>
          ))}
        </Select.Content>
      </Select.Root>

      {value.hasStoredValue && value.mode === 'keep' ? (
        <Text size="2" color="gray">Stored value is configured and remains unchanged.</Text>
      ) : null}

      {value.hasStoredValue && value.mode === 'remove' ? (
        <Text size="2" color="red">
          This will remove the stored value. Save changes to confirm removal.
        </Text>
      ) : null}

      {canShowEditor ? (
        <>
          <Select.Root
            value={value.kind}
            onValueChange={(next) =>
              onChange({
                ...value,
                kind: next as 'inline' | 'ref',
              })
            }
          >
            <Select.Trigger />
            <Select.Content>
              <Select.Item value="inline">Inline secret</Select.Item>
              <Select.Item value="ref">Secret reference</Select.Item>
            </Select.Content>
          </Select.Root>

          <TextField.Root
            type="password"
            autoComplete="new-password"
            spellCheck={false}
            placeholder={value.kind === 'ref' ? 'secret.reference' : 'secret value'}
            value={value.value}
            onChange={(entry) => onChange({ ...value, value: entry.target.value })}
          />

          <TextField.Root
            placeholder="Version (optional)"
            value={value.version}
            onChange={(entry) => onChange({ ...value, version: entry.target.value })}
          />
        </>
      ) : null}

      {value.hasStoredValue ? (
        <Button
          type="button"
          variant="ghost"
          color="gray"
          onClick={() =>
            onChange({
              ...value,
              mode: 'keep',
              value: '',
              version: '',
            })
          }
          disabled={value.mode === 'keep'}
        >
          Reset to stored value
        </Button>
      ) : null}
    </Flex>
  )
}
