import { Select, Text } from '@radix-ui/themes'
import type { DestinationType } from '../../../services/api/contracts-api'

const destinationOptions: DestinationType[] = ['Http', 'Kafka', 'Nats', 'RabbitMq', 'Sqs']

type DestinationTypeSelectorProps = {
  value: DestinationType
  onChange: (value: DestinationType) => void
}

export function DestinationTypeSelector({
  value,
  onChange,
}: DestinationTypeSelectorProps) {
  return (
    <>
      <Text id="destination-type-label" size="2" color="gray">
        Destination type
      </Text>
      <Select.Root
        value={value}
        onValueChange={(next) => onChange(next as DestinationType)}
      >
        <Select.Trigger aria-labelledby="destination-type-label" />
        <Select.Content>
          {destinationOptions.map((option) => (
            <Select.Item key={option} value={option}>
              {option}
            </Select.Item>
          ))}
        </Select.Content>
      </Select.Root>
    </>
  )
}
