import { normalizeConfiguration } from './normalize-configuration.ts'
import { createDefaultDestinationFormState, createDefaultSecretFieldState, mapDestinationFormToConfig, type DestinationFormState } from './types.ts'
import type { DestinationType } from '../../../services/api/contracts-api.ts'

type Credential = { kind?: string; value?: string; version?: number }
type StoredConfiguration = {
  url?: string
  method?: string
  headers?: Record<string, string>
  authentication?: { type?: string; credential?: Credential }
  bootstrapServers?: string
  topic?: string
  clientId?: string
  acks?: string
  timeoutSeconds?: number
  credentials?: {
    securityProtocol?: string; saslMechanism?: string; username?: string
    password?: Credential; token?: Credential; secretAccessKey?: Credential
    sessionToken?: Credential; accessKeyId?: string
  }
  subject?: string
  useJetStream?: boolean
  stream?: string
  uri?: string
  exchange?: string
  routingKey?: string
  persistent?: boolean
  mandatory?: boolean
  region?: string
  queueUrl?: string
  serviceUrl?: string
}

function storedSecret(value?: Credential) {
  return value ? {
    mode: 'keep' as const,
    hasStoredValue: true,
    kind: value.kind === 'ref' ? 'ref' as const : 'inline' as const,
    value: value.value ?? '',
    version: value.version ? String(value.version) : '',
  } : createDefaultSecretFieldState()
}

export function hydrateDestinationForm(type: DestinationType, configuration: string): DestinationFormState {
  const base = createDefaultDestinationFormState(type)
  const config = normalizeConfiguration(JSON.parse(configuration)) as StoredConfiguration
  switch (type) {
    case 'Http': return { ...base, http: {
      url: config.url ?? '',
      method: config.method === 'PUT' || config.method === 'PATCH' || config.method === 'DELETE' ? config.method : 'POST',
      headers: JSON.stringify(config.headers ?? {}, null, 2),
      timeoutSeconds: config.timeoutSeconds?.toString() ?? '',
      authenticationType: config.authentication?.type === 'bearer' || config.authentication?.type === 'hmac'
        ? config.authentication.type : 'none',
      credential: storedSecret(config.authentication?.credential),
    } }
    case 'Kafka': return { ...base, kafka: {
      bootstrapServers: config.bootstrapServers ?? '', topic: config.topic ?? '', clientId: config.clientId ?? '',
      acks: config.acks ?? '', timeoutSeconds: config.timeoutSeconds ? String(config.timeoutSeconds) : '',
      securityProtocol: config.credentials?.securityProtocol ?? '', saslMechanism: config.credentials?.saslMechanism ?? '',
      username: config.credentials?.username ?? '', password: storedSecret(config.credentials?.password),
    } }
    case 'Nats': return { ...base, nats: {
      url: config.url ?? '', subject: config.subject ?? '', timeoutSeconds: config.timeoutSeconds ? String(config.timeoutSeconds) : '',
      useJetStream: config.useJetStream ?? false, stream: config.stream ?? '',
      token: storedSecret(config.credentials?.token), username: config.credentials?.username ?? '',
      password: storedSecret(config.credentials?.password),
    } }
    case 'RabbitMq': return { ...base, rabbitMq: {
      uri: config.uri ?? '', exchange: config.exchange ?? '', routingKey: config.routingKey ?? '',
      persistent: config.persistent ?? true, mandatory: config.mandatory ?? false,
      timeoutSeconds: config.timeoutSeconds ? String(config.timeoutSeconds) : '',
      username: config.credentials?.username ?? '', password: storedSecret(config.credentials?.password),
    } }
    case 'Sqs': return { ...base, sqs: {
      region: config.region ?? '', queueUrl: config.queueUrl ?? '', serviceUrl: config.serviceUrl ?? '',
      timeoutSeconds: config.timeoutSeconds ? String(config.timeoutSeconds) : '',
      accessKeyId: config.credentials?.accessKeyId ?? '', secretAccessKey: storedSecret(config.credentials?.secretAccessKey),
      sessionToken: storedSecret(config.credentials?.sessionToken),
    } }
  }
}

export function mergeDestinationConfiguration(
  storedText: string,
  state: DestinationFormState,
): Record<string, unknown> {
  const stored = normalizeConfiguration(JSON.parse(storedText)) as Record<string, unknown>
  const changes = mapDestinationFormToConfig(state)
  if (changes.type === 'http') {
    const storedAuth = stored.authentication as Record<string, unknown> | undefined
    const merged: Record<string, unknown> = { type: 'http', ...stored, url: changes.url,
      authentication: { type: changes.authenticationType,
        credential: changes.authenticationType === 'none' ? null
          : state.http.credential.mode === 'keep' ? storedAuth?.credential ?? null : changes.credential ?? null } }
    merged.type = 'http'
    merged.method = changes.method
    merged.headers = changes.headers
    merged.timeoutSeconds = changes.timeoutSeconds ?? null
    delete merged.secretHeaders
    delete merged.protectedHeaders
    return merged
  }
  const merged: Record<string, unknown> = { type: changes.type, ...stored }
  Object.assign(merged, changes)
  const originalCredentials = stored.credentials && typeof stored.credentials === 'object'
    ? stored.credentials as Record<string, unknown> : {}
  const changedCredentials = changes.credentials as Record<string, unknown> | undefined
  merged.credentials = changedCredentials || Object.keys(originalCredentials).length > 0
    ? { ...originalCredentials, ...changedCredentials }
    : null
  const credentials = merged.credentials as Record<string, unknown> | null
  if (credentials) {
    const removed = state.type === 'Kafka' ? ['password', state.kafka.password.mode] as const
      : state.type === 'Nats' ? ['token', state.nats.token.mode, 'password', state.nats.password.mode] as const
      : state.type === 'RabbitMq' ? ['password', state.rabbitMq.password.mode] as const
      : ['secretAccessKey', state.sqs.secretAccessKey.mode, 'sessionToken', state.sqs.sessionToken.mode] as const
    for (let index = 0; index < removed.length; index += 2) {
      if (removed[index + 1] === 'remove') credentials[removed[index]] = null
      if (removed[index + 1] === 'keep') credentials[removed[index]] = originalCredentials[removed[index]] ?? null
    }
  }
  return merged
}
