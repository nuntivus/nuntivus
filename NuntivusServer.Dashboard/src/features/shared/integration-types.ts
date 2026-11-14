import type { IntegrationTypeOption } from '../../components/forms/integration-type-picker'
import {
  FolderIntegrationIcon,
  HttpIntegrationIcon,
  KafkaIntegrationIcon,
  NatsIntegrationIcon,
  PollingIntegrationIcon,
  RabbitMqIntegrationIcon,
  S3IntegrationIcon,
  SqsIntegrationIcon,
} from '../../components/icons/integration-icons'
import type { DestinationType } from '../../services/api/contracts-api'

export const destinationTypeOptions: IntegrationTypeOption<DestinationType>[] = [
  { value: 'Http', label: 'HTTP', description: 'Send events to an HTTP endpoint.', icon: HttpIntegrationIcon, color: 'var(--blue-11)' },
  { value: 'Kafka', label: 'Kafka', description: 'Publish events to a Kafka topic.', icon: KafkaIntegrationIcon, color: 'var(--gray-12)' },
  { value: 'Nats', label: 'NATS', description: 'Publish events to a NATS subject.', icon: NatsIntegrationIcon, color: 'var(--teal-11)' },
  { value: 'RabbitMq', label: 'RabbitMQ', description: 'Route messages through an AMQP exchange.', icon: RabbitMqIntegrationIcon, color: 'var(--orange-11)' },
  { value: 'Sqs', label: 'Amazon SQS', description: 'Send messages to an AWS queue.', icon: SqsIntegrationIcon, color: 'var(--purple-11)' },
]

export const sourceTypeOptions: IntegrationTypeOption[] = [
  { value: 'HTTP_API', label: 'HTTP API', description: 'Receive events through an HTTP API.', icon: HttpIntegrationIcon, color: 'var(--blue-11)' },
  { value: 'RABBITMQ', label: 'RabbitMQ', description: 'Consume messages from an AMQP queue.', icon: RabbitMqIntegrationIcon, color: 'var(--orange-11)' },
  { value: 'KAFKA', label: 'Kafka', description: 'Consume events from a Kafka topic.', icon: KafkaIntegrationIcon, color: 'var(--gray-12)' },
  { value: 'SQS', label: 'Amazon SQS', description: 'Receive messages from an AWS queue.', icon: SqsIntegrationIcon, color: 'var(--purple-11)' },
  { value: 'NATS', label: 'NATS', description: 'Consume events from a JetStream stream.', icon: NatsIntegrationIcon, color: 'var(--teal-11)' },
  { value: 'HTTP_POLLING', label: 'HTTP Polling', description: 'Fetch events from an HTTP endpoint on a schedule.', icon: PollingIntegrationIcon, color: 'var(--cyan-11)' },
  { value: 'FOLDER', label: 'Folder', description: 'Read event files from a local directory.', icon: FolderIntegrationIcon, color: 'var(--amber-11)' },
  { value: 'S3', label: 'Amazon S3', description: 'Read event files from an AWS bucket.', icon: S3IntegrationIcon, color: 'var(--green-11)' },
]
