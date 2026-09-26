import type {
  DestinationConfigInput,
  DestinationType,
  DestinationCredentialInput,
} from '../../../services/api/contracts-api'

export type SecretMode = 'replace' | 'keep' | 'remove'

export function normalizeDestinationType(value: unknown): DestinationType | null {
  switch (value) {
    case 1:
    case 'Http':
    case 'http':
      return 'Http'
    case 2:
    case 'Kafka':
    case 'kafka':
      return 'Kafka'
    case 3:
    case 'Nats':
    case 'nats':
      return 'Nats'
    case 4:
    case 'RabbitMq':
    case 'rabbitmq':
      return 'RabbitMq'
    case 5:
    case 'Sqs':
    case 'sqs':
      return 'Sqs'
    default:
      return null
  }
}

type SecretFieldState = {
  mode: SecretMode
  hasStoredValue: boolean
  kind: 'inline' | 'ref'
  value: string
  version: string
}

export type DestinationFormState = {
  type: DestinationType
  http: {
    url: string
    authenticationType: 'none' | 'bearer' | 'hmac'
    credential: SecretFieldState
  }
  kafka: {
    bootstrapServers: string
    topic: string
    clientId: string
    acks: string
    timeoutSeconds: string
    securityProtocol: string
    saslMechanism: string
    username: string
    password: SecretFieldState
  }
  nats: {
    url: string
    subject: string
    timeoutSeconds: string
    useJetStream: boolean
    stream: string
    token: SecretFieldState
    username: string
    password: SecretFieldState
  }
  rabbitMq: {
    uri: string
    exchange: string
    routingKey: string
    persistent: boolean
    mandatory: boolean
    timeoutSeconds: string
    username: string
    password: SecretFieldState
  }
  sqs: {
    region: string
    queueUrl: string
    serviceUrl: string
    timeoutSeconds: string
    accessKeyId: string
    secretAccessKey: SecretFieldState
    sessionToken: SecretFieldState
  }
}

export function createDefaultSecretFieldState(): SecretFieldState {
  return {
    mode: 'replace',
    hasStoredValue: false,
    kind: 'inline',
    value: '',
    version: '',
  }
}

export function createDefaultDestinationFormState(
  type: DestinationType = 'Http',
): DestinationFormState {
  return {
    type,
    http: {
      url: '',
      authenticationType: 'none',
      credential: createDefaultSecretFieldState(),
    },
    kafka: {
      bootstrapServers: '',
      topic: '',
      clientId: '',
      acks: '',
      timeoutSeconds: '',
      securityProtocol: '',
      saslMechanism: '',
      username: '',
      password: createDefaultSecretFieldState(),
    },
    nats: {
      url: '',
      subject: '',
      timeoutSeconds: '',
      useJetStream: false,
      stream: '',
      token: createDefaultSecretFieldState(),
      username: '',
      password: createDefaultSecretFieldState(),
    },
    rabbitMq: {
      uri: '',
      exchange: '',
      routingKey: '',
      persistent: true,
      mandatory: false,
      timeoutSeconds: '',
      username: '',
      password: createDefaultSecretFieldState(),
    },
    sqs: {
      region: '',
      queueUrl: '',
      serviceUrl: '',
      timeoutSeconds: '',
      accessKeyId: '',
      secretAccessKey: createDefaultSecretFieldState(),
      sessionToken: createDefaultSecretFieldState(),
    },
  }
}

function parsePositiveInt(value: string): number | undefined {
  const trimmed = value.trim()
  if (!trimmed) {
    return undefined
  }

  const parsed = Number.parseInt(trimmed, 10)
  if (Number.isNaN(parsed) || parsed <= 0) {
    return undefined
  }

  return parsed
}

function toCredential(
  secret: SecretFieldState,
): DestinationCredentialInput | undefined {
  if (secret.mode === 'remove') {
    return undefined
  }

  if (secret.mode === 'keep') {
    return secret.hasStoredValue
      ? {
          kind: secret.kind,
          value: secret.value,
          version: parsePositiveInt(secret.version),
        }
      : undefined
  }

  const trimmed = secret.value.trim()
  if (!trimmed) {
    return undefined
  }

  return {
    kind: secret.kind,
    value: trimmed,
    version: parsePositiveInt(secret.version),
  }
}

export function mapDestinationFormToConfig(
  state: DestinationFormState,
): DestinationConfigInput {
  switch (state.type) {
    case 'Http':
      return {
        type: 'http',
        url: state.http.url.trim(),
        authenticationType: state.http.authenticationType,
        credential:
          state.http.authenticationType === 'none'
            ? undefined
            : toCredential(state.http.credential),
      }

    case 'Kafka':
      return {
        type: 'kafka',
        bootstrapServers: state.kafka.bootstrapServers.trim(),
        topic: state.kafka.topic.trim(),
        clientId: state.kafka.clientId.trim() || undefined,
        acks: state.kafka.acks.trim() || undefined,
        timeoutSeconds: parsePositiveInt(state.kafka.timeoutSeconds),
        credentials:
          state.kafka.securityProtocol.trim() ||
          state.kafka.saslMechanism.trim() ||
          state.kafka.username.trim() ||
          state.kafka.password.value.trim() ||
          state.kafka.password.mode === 'keep'
            ? {
                securityProtocol: state.kafka.securityProtocol.trim() || undefined,
                saslMechanism: state.kafka.saslMechanism.trim() || undefined,
                username: state.kafka.username.trim() || undefined,
                password: toCredential(state.kafka.password),
              }
            : undefined,
      }

    case 'Nats':
      return {
        type: 'nats',
        url: state.nats.url.trim(),
        subject: state.nats.subject.trim(),
        timeoutSeconds: parsePositiveInt(state.nats.timeoutSeconds),
        useJetStream: state.nats.useJetStream,
        stream: state.nats.stream.trim() || undefined,
        credentials:
          state.nats.token.value.trim() ||
          state.nats.username.trim() ||
          state.nats.password.value.trim() ||
          state.nats.token.mode === 'keep' ||
          state.nats.password.mode === 'keep'
            ? {
                token: toCredential(state.nats.token),
                username: state.nats.username.trim() || undefined,
                password: toCredential(state.nats.password),
              }
            : undefined,
      }

    case 'RabbitMq':
      return {
        type: 'rabbitmq',
        uri: state.rabbitMq.uri.trim(),
        exchange: state.rabbitMq.exchange.trim(),
        routingKey: state.rabbitMq.routingKey.trim(),
        persistent: state.rabbitMq.persistent,
        mandatory: state.rabbitMq.mandatory,
        timeoutSeconds: parsePositiveInt(state.rabbitMq.timeoutSeconds),
        credentials:
          state.rabbitMq.username.trim() ||
          state.rabbitMq.password.value.trim() ||
          state.rabbitMq.password.mode === 'keep'
            ? {
                username: state.rabbitMq.username.trim() || undefined,
                password: toCredential(state.rabbitMq.password),
              }
            : undefined,
      }

    case 'Sqs':
      return {
        type: 'sqs',
        region: state.sqs.region.trim(),
        queueUrl: state.sqs.queueUrl.trim(),
        serviceUrl: state.sqs.serviceUrl.trim() || undefined,
        timeoutSeconds: parsePositiveInt(state.sqs.timeoutSeconds),
        credentials:
          state.sqs.accessKeyId.trim() ||
          state.sqs.secretAccessKey.value.trim() ||
          state.sqs.sessionToken.value.trim() ||
          state.sqs.secretAccessKey.mode === 'keep' ||
          state.sqs.sessionToken.mode === 'keep'
            ? {
                accessKeyId: state.sqs.accessKeyId.trim() || undefined,
                secretAccessKey: toCredential(state.sqs.secretAccessKey),
                sessionToken: toCredential(state.sqs.sessionToken),
              }
            : undefined,
      }
  }
}

export function validateDestinationFormState(state: DestinationFormState): string | null {
  switch (state.type) {
    case 'Http': {
      if (!state.http.url.trim()) {
        return 'Destination URL is required.'
      }

      try {
        const parsed = new URL(state.http.url)
        if (!['http:', 'https:'].includes(parsed.protocol)) {
          return 'Destination URL must use http or https.'
        }
      } catch {
        return 'Destination URL is invalid.'
      }

      if (
        state.http.authenticationType !== 'none' &&
        state.http.credential.mode === 'replace' &&
        !state.http.credential.value.trim()
      ) {
        return 'Credential value is required for selected authentication.'
      }

      if (
        state.http.authenticationType !== 'none' &&
        state.http.credential.mode === 'replace' &&
        state.http.credential.version.trim() &&
        parsePositiveInt(state.http.credential.version) === undefined
      ) {
        return 'Credential version must be a positive number.'
      }

      return null
    }

    case 'Kafka': {
      if (!state.kafka.bootstrapServers.trim()) {
        return 'Kafka bootstrap servers are required.'
      }

      if (!state.kafka.topic.trim()) {
        return 'Kafka topic is required.'
      }

      if (
        state.kafka.username.trim() &&
        state.kafka.password.mode === 'replace' &&
        !state.kafka.password.value.trim()
      ) {
        return 'Kafka password is required when username is provided.'
      }

      if (
        state.kafka.password.mode === 'replace' &&
        state.kafka.password.version.trim() &&
        parsePositiveInt(state.kafka.password.version) === undefined
      ) {
        return 'Kafka password version must be a positive number.'
      }

      return null
    }

    case 'Nats': {
      if (!state.nats.url.trim()) {
        return 'NATS URL is required.'
      }

      try {
        const parsed = new URL(state.nats.url)
        if (!['nats:', 'tls:'].includes(parsed.protocol)) {
          return 'NATS URL must use nats or tls protocol.'
        }
      } catch {
        return 'NATS URL is invalid.'
      }

      if (!state.nats.subject.trim()) {
        return 'NATS subject is required.'
      }

      if (
        state.nats.username.trim() &&
        state.nats.password.mode === 'replace' &&
        !state.nats.password.value.trim()
      ) {
        return 'NATS password is required when username is provided.'
      }

      if (
        state.nats.token.mode === 'replace' &&
        state.nats.token.version.trim() &&
        parsePositiveInt(state.nats.token.version) === undefined
      ) {
        return 'NATS token version must be a positive number.'
      }

      if (
        state.nats.password.mode === 'replace' &&
        state.nats.password.version.trim() &&
        parsePositiveInt(state.nats.password.version) === undefined
      ) {
        return 'NATS password version must be a positive number.'
      }

      return null
    }

    case 'RabbitMq': {
      if (!state.rabbitMq.uri.trim()) {
        return 'RabbitMQ URI is required.'
      }

      try {
        const parsed = new URL(state.rabbitMq.uri)
        if (!['amqp:', 'amqps:'].includes(parsed.protocol)) {
          return 'RabbitMQ URI must use amqp or amqps protocol.'
        }
      } catch {
        return 'RabbitMQ URI is invalid.'
      }

      if (!state.rabbitMq.exchange.trim()) {
        return 'RabbitMQ exchange is required.'
      }

      if (!state.rabbitMq.routingKey.trim()) {
        return 'RabbitMQ routing key is required.'
      }

      if (
        state.rabbitMq.username.trim() &&
        state.rabbitMq.password.mode === 'replace' &&
        !state.rabbitMq.password.value.trim()
      ) {
        return 'RabbitMQ password is required when username is provided.'
      }

      if (
        state.rabbitMq.password.mode === 'replace' &&
        state.rabbitMq.password.version.trim() &&
        parsePositiveInt(state.rabbitMq.password.version) === undefined
      ) {
        return 'RabbitMQ password version must be a positive number.'
      }

      return null
    }

    case 'Sqs': {
      if (!state.sqs.region.trim()) {
        return 'SQS region is required.'
      }

      if (!state.sqs.queueUrl.trim()) {
        return 'SQS queue URL is required.'
      }

      try {
        new URL(state.sqs.queueUrl)
      } catch {
        return 'SQS queue URL is invalid.'
      }

      if (state.sqs.serviceUrl.trim()) {
        try {
          new URL(state.sqs.serviceUrl)
        } catch {
          return 'SQS service URL is invalid.'
        }
      }

      if (
        state.sqs.secretAccessKey.mode === 'replace' &&
        state.sqs.secretAccessKey.version.trim() &&
        parsePositiveInt(state.sqs.secretAccessKey.version) === undefined
      ) {
        return 'SQS secret access key version must be a positive number.'
      }

      if (
        state.sqs.sessionToken.mode === 'replace' &&
        state.sqs.sessionToken.version.trim() &&
        parsePositiveInt(state.sqs.sessionToken.version) === undefined
      ) {
        return 'SQS session token version must be a positive number.'
      }

      return null
    }
  }
}
