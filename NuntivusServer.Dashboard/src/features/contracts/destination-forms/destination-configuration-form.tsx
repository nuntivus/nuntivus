import { Flex } from '@radix-ui/themes'
import { DestinationTypeSelector } from './destination-type-selector'
import { HttpDestinationForm } from './http-destination-form'
import { KafkaDestinationForm } from './kafka-destination-form'
import { NatsDestinationForm } from './nats-destination-form'
import { RabbitMqDestinationForm } from './rabbitmq-destination-form'
import { SqsDestinationForm } from './sqs-destination-form'
import type { DestinationFormState } from './types'

type DestinationConfigurationFormProps = {
  value: DestinationFormState
  onChange: (next: DestinationFormState) => void
  fixedType?: boolean
}

export function DestinationConfigurationForm({
  value,
  onChange,
  fixedType = false,
}: DestinationConfigurationFormProps) {
  return (
    <Flex direction="column" gap="3" className="configuration-form-grid">
      {!fixedType && <DestinationTypeSelector
        value={value.type}
        onChange={(type) => onChange({ ...value, type })}
      />}

      {value.type === 'Http' ? (
        <HttpDestinationForm
          value={value.http}
          onChange={(http) => onChange({ ...value, http })}
        />
      ) : null}

      {value.type === 'Kafka' ? (
        <KafkaDestinationForm
          value={value.kafka}
          onChange={(kafka) => onChange({ ...value, kafka })}
        />
      ) : null}

      {value.type === 'Nats' ? (
        <NatsDestinationForm
          value={value.nats}
          onChange={(nats) => onChange({ ...value, nats })}
        />
      ) : null}

      {value.type === 'RabbitMq' ? (
        <RabbitMqDestinationForm
          value={value.rabbitMq}
          onChange={(rabbitMq) => onChange({ ...value, rabbitMq })}
        />
      ) : null}

      {value.type === 'Sqs' ? (
        <SqsDestinationForm
          value={value.sqs}
          onChange={(sqs) => onChange({ ...value, sqs })}
        />
      ) : null}
    </Flex>
  )
}
