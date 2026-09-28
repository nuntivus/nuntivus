import { Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import type { DestinationFormState } from './types'

type KafkaDestinationFormProps = {
  value: DestinationFormState['kafka']
  onChange: (next: DestinationFormState['kafka']) => void
}

export function KafkaDestinationForm({
  value,
  onChange,
}: KafkaDestinationFormProps) {
  return (
    <>
      <div className="destination-form-field">
        <Text size="2" color="gray">Bootstrap servers</Text>
        <TextField.Root
          placeholder="broker1:9092,broker2:9092"
          value={value.bootstrapServers}
          onChange={(entry) => onChange({ ...value, bootstrapServers: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Topic</Text>
        <TextField.Root
          placeholder="orders-events"
          value={value.topic}
          onChange={(entry) => onChange({ ...value, topic: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Client ID</Text>
        <TextField.Root
          placeholder="nuntivus-producer"
          value={value.clientId}
          onChange={(entry) => onChange({ ...value, clientId: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Acks</Text>
        <TextField.Root
          placeholder="all"
          value={value.acks}
          onChange={(entry) => onChange({ ...value, acks: entry.target.value })}
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
        <Text size="2" color="gray">Security protocol</Text>
        <TextField.Root
          placeholder="SASL_SSL"
          value={value.securityProtocol}
          onChange={(entry) => onChange({ ...value, securityProtocol: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">SASL mechanism</Text>
        <TextField.Root
          placeholder="PLAIN"
          value={value.saslMechanism}
          onChange={(entry) => onChange({ ...value, saslMechanism: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Username</Text>
        <TextField.Root
          placeholder="kafka-user"
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
