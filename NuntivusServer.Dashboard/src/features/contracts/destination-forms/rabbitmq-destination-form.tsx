import { Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import type { DestinationFormState } from './types'

type RabbitMqDestinationFormProps = {
  value: DestinationFormState['rabbitMq']
  onChange: (next: DestinationFormState['rabbitMq']) => void
}

export function RabbitMqDestinationForm({
  value,
  onChange,
}: RabbitMqDestinationFormProps) {
  return (
    <>
      <div className="destination-form-field">
        <Text size="2" color="gray">URI</Text>
        <TextField.Root
          placeholder="amqps://broker:5671/vhost"
          value={value.uri}
          onChange={(entry) => onChange({ ...value, uri: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Exchange</Text>
        <TextField.Root
          placeholder="orders"
          value={value.exchange}
          onChange={(entry) => onChange({ ...value, exchange: entry.target.value })}
        />
      </div>

      <div className="destination-form-field">
        <Text size="2" color="gray">Routing key</Text>
        <TextField.Root
          placeholder="orders.created"
          value={value.routingKey}
          onChange={(entry) => onChange({ ...value, routingKey: entry.target.value })}
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
        <Text size="2" color="gray">Username</Text>
        <TextField.Root
          placeholder="rabbit-user"
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
