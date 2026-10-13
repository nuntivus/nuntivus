import { Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import type { DestinationFormState } from './types'

type NatsDestinationFormProps = {
  value: DestinationFormState['nats']
  onChange: (next: DestinationFormState['nats']) => void
}

export function NatsDestinationForm({ value, onChange }: NatsDestinationFormProps) {
  return (
    <>
      <div className="destination-form-field configuration-form-wide">
        <Text size="2" color="gray">NATS URL</Text>
        <TextField.Root
          placeholder="nats://localhost:4222"
          value={value.url}
          onChange={(entry) => onChange({ ...value, url: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Subject</Text>
        <TextField.Root
          placeholder="orders.created"
          value={value.subject}
          onChange={(entry) => onChange({ ...value, subject: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Timeout seconds</Text>
        <TextField.Root
          placeholder="30"
          value={value.timeoutSeconds}
          onChange={(entry) => onChange({ ...value, timeoutSeconds: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Stream (JetStream optional)</Text>
        <TextField.Root
          placeholder="ORDERS_STREAM"
          value={value.stream}
          onChange={(entry) => onChange({ ...value, stream: entry.target.value })}
        />
      </div>

      <SecretField
        label="Token"
        value={value.token}
        onChange={(token) => onChange({ ...value, token })}
      />

      <div className="destination-form-field">
        <Text size="2" color="gray">Username</Text>
        <TextField.Root
          placeholder="nats-user"
          value={value.username}
          onChange={(entry) => onChange({ ...value, username: entry.target.value })}
        />
      </div>

      <SecretField
        label="Password"
        value={value.password}
        onChange={(password) => onChange({ ...value, password })}
      />
    </>
  )
}
