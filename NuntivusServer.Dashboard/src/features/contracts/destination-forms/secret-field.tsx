import { Button, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { useMemo, useState } from 'react'
import { useCreateSecretMutation, useSecretsQuery } from '../use-secrets'
import { toManagedSecretReference } from '../managed-secret-name'
import { getApiErrorMessage } from '../../../services/api/api-client'
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
  const secrets = useSecretsQuery()
  const createSecret = useCreateSecretMutation()
  const [creating, setCreating] = useState(false)
  const [newName, setNewName] = useState('')
  const [newValue, setNewValue] = useState('')
  const [createError, setCreateError] = useState<string | null>(null)
  const newReference = toManagedSecretReference(newName)

  async function handleCreate() {
    if (!newReference || !newValue.trim() || createSecret.isPending) return
    setCreateError(null)
    try {
      const created = await createSecret.mutateAsync({ name: newReference, value: newValue })
      onChange({ ...value, kind: 'ref', value: created.name })
      setNewName('')
      setNewValue('')
      setCreating(false)
    } catch (error) {
      setCreateError(getApiErrorMessage(error, 'Unable to store secret.'))
    }
  }

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
    <Flex direction="column" gap="2" className="destination-form-field configuration-form-wide">
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
                value: '',
                version: next === 'ref' ? '' : value.version,
              })
            }
          >
            <Select.Trigger />
            <Select.Content>
              <Select.Item value="inline">Inline secret</Select.Item>
              <Select.Item value="ref">Secret reference</Select.Item>
            </Select.Content>
          </Select.Root>

          {value.kind === 'ref' ? (
            <>
              <Select.Root value={(secrets.data ?? []).some((secret) => secret.name === value.value) ? value.value : undefined} onValueChange={(next) => onChange({ ...value, value: next })}>
                <Select.Trigger placeholder="Select stored secret" />
                <Select.Content>
                  {(secrets.data ?? []).map((secret) => <Select.Item key={secret.name} value={secret.name}>{secret.name}</Select.Item>)}
                </Select.Content>
              </Select.Root>
              {secrets.isError ? <Text size="2" color="red">Unable to load stored secrets.</Text> : null}
              <Button type="button" variant="ghost" onClick={() => setCreating(!creating)}>Create stored secret</Button>
              {creating ? (
                <Flex direction="column" gap="2">
                  <TextField.Root placeholder="acme.webhook" aria-label="New secret name" value={newName} onChange={(entry) => setNewName(entry.target.value)} />
                  <Text size="1" color={newName.trim() && !newReference ? 'red' : 'gray'}>
                    {newName.trim() && !newReference
                      ? 'Use dot-separated letters, numbers, underscores or hyphens.'
                      : `Stored as ${newReference ?? 'secret.acme.webhook'} (the secret. prefix is optional).`}
                  </Text>
                  <TextField.Root type="password" autoComplete="new-password" placeholder="Secret value" aria-label="New secret value" value={newValue} onChange={(entry) => setNewValue(entry.target.value)} />
                  <Button type="button" disabled={createSecret.isPending || !newReference || !newValue.trim()} onClick={() => void handleCreate()}>Store and select</Button>
                  {createError ? <Text size="2" color="red">{createError}</Text> : null}
                </Flex>
              ) : null}
            </>
          ) : (
            <TextField.Root
              type="password"
              autoComplete="new-password"
              spellCheck={false}
              placeholder="secret value"
              value={value.value}
              onChange={(entry) => onChange({ ...value, value: entry.target.value })}
            />
          )}

          {value.kind === 'inline' ? (
            <TextField.Root
              placeholder="Version (optional)"
              value={value.version}
              onChange={(entry) => onChange({ ...value, version: entry.target.value })}
            />
          ) : null}
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
