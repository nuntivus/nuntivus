import { Button, Select, Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import { httpHeaderRows, type DestinationFormState } from './types'

type HttpDestinationFormProps = {
  value: DestinationFormState['http']
  onChange: (next: DestinationFormState['http']) => void
}

export function HttpDestinationForm({ value, onChange }: HttpDestinationFormProps) {
  const headers = httpHeaderRows(value.headers)

  function changeHeaders(next: typeof headers) {
    onChange({ ...value, headers: JSON.stringify(next) })
  }

  return (
    <>
      <div className="destination-form-field configuration-form-wide">
        <Text as="label" htmlFor="http-destination-url" size="2" color="gray">Destination URL</Text>
        <TextField.Root id="http-destination-url" placeholder="https://example.com/webhook" value={value.url} onChange={(event) => onChange({ ...value, url: event.target.value })} />
        <Text size="1" color="gray">The full URL that receives delivered events.</Text>
      </div>

      <div className="destination-form-field">
        <Text id="http-method-label" size="2" color="gray">Delivery method</Text>
        <Select.Root value={value.method} onValueChange={(method) => onChange({ ...value, method: method as typeof value.method })}>
          <Select.Trigger aria-labelledby="http-method-label" /><Select.Content>
            {(['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const).map(method => <Select.Item key={method} value={method}>{method}</Select.Item>)}
          </Select.Content>
        </Select.Root>
        <Text size="1" color="gray">The HTTP method used to deliver each event. GET sends no request body.</Text>
      </div>
      <div className="destination-form-field">
        <Text as="label" htmlFor="http-destination-timeout" size="2" color="gray">Timeout (seconds, 1–300)</Text>
        <TextField.Root id="http-destination-timeout" type="number" min="1" max="300" value={value.timeoutSeconds} onChange={(event) => onChange({ ...value, timeoutSeconds: event.target.value })} />
        <Text size="1" color="gray">Maximum time to wait for a delivery response.</Text>
      </div>
      <div className="destination-form-field configuration-form-wide">
        <Text size="2" color="gray">Plain headers</Text>
        {headers.map((header, index) => <div key={index} className="http-header-row">
          <TextField.Root aria-label={`Header ${index + 1} key`} placeholder="Header name" value={header.key}
            onChange={(event) => changeHeaders(headers.map((row, rowIndex) => rowIndex === index ? { ...row, key: event.target.value } : row))} />
          <TextField.Root aria-label={`Header ${index + 1} value`} placeholder="Value" value={header.value}
            onChange={(event) => changeHeaders(headers.map((row, rowIndex) => rowIndex === index ? { ...row, value: event.target.value } : row))} />
          <Button type="button" variant="soft" color="red" aria-label={`Remove header ${index + 1}`} onClick={() => changeHeaders(headers.filter((_, rowIndex) => rowIndex !== index))}>Remove</Button>
        </div>)}
        <Button type="button" variant="soft" style={{ alignSelf: 'start' }} onClick={() => changeHeaders([...headers, { key: '', value: '' }])}>Add header</Button>
        <Text size="1" color="gray">Do not enter secrets here; plain headers are visible in configuration.</Text>
      </div>

      <div className="destination-form-field">
        <Text id="http-authentication-label" size="2" color="gray">Authentication</Text>
        <Select.Root value={value.authenticationType} onValueChange={(next) => onChange({ ...value, authenticationType: next as 'none' | 'bearer' | 'hmac' })}>
          <Select.Trigger aria-labelledby="http-authentication-label" />
          <Select.Content>
            <Select.Item value="none">None</Select.Item>
            <Select.Item value="bearer">Bearer</Select.Item>
            <Select.Item value="hmac">HMAC</Select.Item>
          </Select.Content>
        </Select.Root>
      </div>

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
