import { Select, Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import type { DestinationFormState } from './types'

type HttpDestinationFormProps = {
  value: DestinationFormState['http']
  onChange: (next: DestinationFormState['http']) => void
}

export function HttpDestinationForm({ value, onChange }: HttpDestinationFormProps) {
  return (
    <>
      <Text as="label" htmlFor="http-destination-url" size="2" color="gray">
        Destination URL
      </Text>
      <TextField.Root
        id="http-destination-url"
        placeholder="https://example.com/webhook"
        value={value.url}
        onChange={(event) => onChange({ ...value, url: event.target.value })}
      />

      <Text id="http-authentication-label" size="2" color="gray">
        Authentication
      </Text>
      <Select.Root
        value={value.authenticationType}
        onValueChange={(next) =>
          onChange({
            ...value,
            authenticationType: next as 'none' | 'bearer' | 'hmac',
          })
        }
      >
        <Select.Trigger aria-labelledby="http-authentication-label" />
        <Select.Content>
          <Select.Item value="none">None</Select.Item>
          <Select.Item value="bearer">Bearer</Select.Item>
          <Select.Item value="hmac">HMAC</Select.Item>
        </Select.Content>
      </Select.Root>

      {value.authenticationType !== 'none' ? (
        <SecretField
          label="Credential"
          value={value.credential}
          onChange={(credential) => onChange({ ...value, credential })}
        />
      ) : null}
    </>
  )
}
