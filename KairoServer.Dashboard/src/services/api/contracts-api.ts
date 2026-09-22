import { apiGet, apiGetWithResponse, apiPost, apiPutWithResponse } from './api-client'

export type DestinationType = 'Http' | 'Kafka' | 'Nats' | 'RabbitMq' | 'Sqs'

export interface Contract {
  id: string
  customerId: string
  enabled: boolean
  eventTypes: string[]
  destinationType: DestinationType
  destinationConfiguration: string
  createdAt: string
  updatedAt: string
  name?: string | null
}

export interface ContractDetail {
  id: string
  customerId: string
  enabled: boolean
  eventTypes: string[]
  destinationType: DestinationType
  destinationConfiguration: string
  createdAt: string
  updatedAt: string
  name?: string | null
}

export interface PagedResponse<TItem> {
  items: TItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export interface ContractListQuery {
  customerId?: string
  enabled?: boolean
  destinationType?: DestinationType
  search?: string
  sortBy?: 'createdAt' | 'updatedAt' | 'name' | 'customerId' | 'destinationType'
  sortDirection?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export interface CreateContractRequest {
  customerId: string
  eventTypes: string[]
  name?: string | null
  destination?: DestinationConfigInput

  // Legacy HTTP-only fields kept temporarily for backward compatibility while pages migrate.
  url: string
  authenticationType: 'none' | 'bearer' | 'hmac'
  credentialKind?: 'inline' | 'ref'
  credentialValue?: string
  credentialVersion?: number
}

export type UpdateContractRequest = CreateContractRequest

export interface ContractDetailResponse {
  contract: ContractDetail
  etag: string | null
}

export interface DeliveryListItem {
  id: string
  eventId: string
  customerId: string
  contractId: string
  eventType: string
  status: number | string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export interface DestinationCredentialInput {
  kind: 'inline' | 'ref'
  value: string
  version?: number
}

export interface HttpDestinationInput {
  type: 'http'
  url: string
  authenticationType: 'none' | 'bearer' | 'hmac'
  credential?: DestinationCredentialInput
  headers?: Record<string, string>
  secretHeaders?: Record<string, string>
  protectedHeaders?: Record<string, string>
  timeoutSeconds?: number
}

export interface KafkaDestinationInput {
  type: 'kafka'
  bootstrapServers: string
  topic: string
  clientId?: string
  acks?: string
  timeoutSeconds?: number
  credentials?: {
    securityProtocol?: string
    saslMechanism?: string
    username?: string
    password?: DestinationCredentialInput
  }
}

export interface NatsDestinationInput {
  type: 'nats'
  url: string
  subject: string
  timeoutSeconds?: number
  useJetStream?: boolean
  stream?: string
  credentials?: {
    token?: DestinationCredentialInput
    username?: string
    password?: DestinationCredentialInput
  }
}

export interface RabbitMqDestinationInput {
  type: 'rabbitmq'
  uri: string
  exchange: string
  routingKey: string
  persistent?: boolean
  mandatory?: boolean
  timeoutSeconds?: number
  credentials?: {
    username?: string
    password?: DestinationCredentialInput
  }
}

export interface SqsDestinationInput {
  type: 'sqs'
  region: string
  queueUrl: string
  serviceUrl?: string
  timeoutSeconds?: number
  credentials?: {
    accessKeyId?: string
    secretAccessKey?: DestinationCredentialInput
    sessionToken?: DestinationCredentialInput
  }
}

export type DestinationConfigInput =
  | HttpDestinationInput
  | KafkaDestinationInput
  | NatsDestinationInput
  | RabbitMqDestinationInput
  | SqsDestinationInput

function toCredential(
  credential: DestinationCredentialInput | undefined,
) {
  if (!credential || !credential.value?.trim()) {
    return null
  }

  return {
    kind: credential.kind,
    value: credential.value.trim(),
    version: credential.version,
  }
}

function buildDestinationConfig(request: CreateContractRequest): DestinationConfigInput {
  if (request.destination) {
    return request.destination
  }

  const credential =
    request.authenticationType !== 'none' &&
    request.credentialKind &&
    request.credentialValue
      ? {
          kind: request.credentialKind,
          value: request.credentialValue,
          version: request.credentialVersion,
        }
      : undefined

  return {
    type: 'http',
    url: request.url,
    authenticationType: request.authenticationType,
    credential,
  }
}

function mapDestinationToApi(destination: DestinationConfigInput) {
  switch (destination.type) {
    case 'http':
      return {
        type: 'http',
        url: destination.url,
        authentication: {
          type: destination.authenticationType,
          credential: toCredential(destination.credential),
        },
        headers: destination.headers,
        secretHeaders: destination.secretHeaders,
        protectedHeaders: destination.protectedHeaders,
        timeoutSeconds: destination.timeoutSeconds,
      }

    case 'kafka':
      return {
        type: 'kafka',
        bootstrapServers: destination.bootstrapServers,
        topic: destination.topic,
        clientId: destination.clientId,
        acks: destination.acks,
        timeoutSeconds: destination.timeoutSeconds,
        credentials: destination.credentials
          ? {
              securityProtocol: destination.credentials.securityProtocol,
              saslMechanism: destination.credentials.saslMechanism,
              username: destination.credentials.username,
              password: toCredential(destination.credentials.password),
            }
          : null,
      }

    case 'nats':
      return {
        type: 'nats',
        url: destination.url,
        subject: destination.subject,
        timeoutSeconds: destination.timeoutSeconds,
        useJetStream: destination.useJetStream ?? false,
        stream: destination.stream,
        credentials: destination.credentials
          ? {
              token: toCredential(destination.credentials.token),
              username: destination.credentials.username,
              password: toCredential(destination.credentials.password),
            }
          : null,
      }

    case 'rabbitmq':
      return {
        type: 'rabbitmq',
        uri: destination.uri,
        exchange: destination.exchange,
        routingKey: destination.routingKey,
        persistent: destination.persistent ?? true,
        mandatory: destination.mandatory ?? false,
        timeoutSeconds: destination.timeoutSeconds,
        credentials: destination.credentials
          ? {
              username: destination.credentials.username,
              password: toCredential(destination.credentials.password),
            }
          : null,
      }

    case 'sqs':
      return {
        type: 'sqs',
        region: destination.region,
        queueUrl: destination.queueUrl,
        serviceUrl: destination.serviceUrl,
        timeoutSeconds: destination.timeoutSeconds,
        credentials: destination.credentials
          ? {
              accessKeyId: destination.credentials.accessKeyId,
              secretAccessKey: toCredential(destination.credentials.secretAccessKey),
              sessionToken: toCredential(destination.credentials.sessionToken),
            }
          : null,
      }
  }
}

export interface FailedDeliveryItem {
  deliveryId: string
  eventId: string
  contractId: string
  customerId: string
  eventType: string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export async function listContracts(query: ContractListQuery) {
  return apiGet<PagedResponse<Contract>>('/v1/admin/contracts', {
    customerId: query.customerId?.trim() ? query.customerId.trim() : null,
    enabled: typeof query.enabled === 'boolean' ? query.enabled : null,
    destinationType: query.destinationType ?? null,
    search: query.search?.trim() ? query.search.trim() : null,
    sortBy: query.sortBy ?? null,
    sortDirection: query.sortDirection ?? null,
    page: query.page,
    pageSize: query.pageSize,
  })
}

export async function createContract(request: CreateContractRequest) {
  const destination = buildDestinationConfig(request)

  return apiPost<Contract>('/v1/contracts', {
    customerId: request.customerId,
    eventTypes: request.eventTypes,
    configuration: mapDestinationToApi(destination),
    name: request.name ?? null,
  })
}

export async function getContractById(id: string): Promise<ContractDetailResponse> {
  const response = await apiGetWithResponse<ContractDetail>(
    `/v1/contracts/${encodeURIComponent(id)}`,
  )

  return {
    contract: response.data,
    etag: response.headers.get('etag'),
  }
}

export async function replaceContract(
  id: string,
  etag: string,
  request: UpdateContractRequest,
) {
  const destination = buildDestinationConfig(request)

  const response = await apiPutWithResponse<ContractDetail>(
    `/v1/contracts/${encodeURIComponent(id)}`,
    {
      name: request.name ?? null,
      eventTypes: request.eventTypes,
      configuration: mapDestinationToApi(destination),
    },
    {
      headers: {
        'If-Match': etag,
      },
    },
  )

  return {
    contract: response.data,
    etag: response.headers.get('etag'),
  }
}

export async function enableContract(id: string) {
  return apiPost<Contract>(`/v1/contracts/${encodeURIComponent(id)}/enable`)
}

export async function disableContract(id: string) {
  return apiPost<Contract>(`/v1/contracts/${encodeURIComponent(id)}/disable`)
}

export async function listContractDeliveries(id: string, pageSize = 10) {
  return apiGet<PagedResponse<DeliveryListItem>>('/v1/deliveries', {
    contractId: id,
    page: 1,
    pageSize,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  })
}

export async function listContractRecentFailures(id: string, pageSize = 10) {
  return apiGet<PagedResponse<FailedDeliveryItem>>('/v1/admin/deliveries/failures', {
    contractId: id,
    page: 1,
    pageSize,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  })
}
