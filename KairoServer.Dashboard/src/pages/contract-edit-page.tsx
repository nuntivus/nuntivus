import {
  Button,
  Card,
  Flex,
  Heading,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { getServerValidation } from '../components/forms/form-validation'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import {
  createDefaultSecretFieldState,
  createDefaultDestinationFormState,
  mapDestinationFormToConfig,
  type DestinationFormState,
  validateDestinationFormState,
} from '../features/contracts/destination-forms/types'
import {
  useContractDetailQuery,
  useReplaceContractMutation,
} from '../features/contracts/use-contracts'
import { getApiErrorMessage } from '../services/api/api-client'
import type { DestinationType } from '../services/api/contracts-api'
import { useAppSelector } from '../store/hooks'

type EditorInitialState = {
  name: string
  eventTypesInput: string
  destination: DestinationFormState
}

function parseEventTypes(value: string) {
  return value
    .split(/[\n,]+/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)
}

function hasSecretMutation(secret: {
  mode: 'replace' | 'keep' | 'remove'
  hasStoredValue: boolean
  value: string
}) {
  if (secret.mode === 'remove') {
    return true
  }

  if (secret.mode === 'replace') {
    return secret.hasStoredValue || secret.value.trim().length > 0
  }

  return false
}

function hasCredentialRotationChange(state: DestinationFormState) {
  switch (state.type) {
    case 'Http':
      return state.http.authenticationType !== 'none' && hasSecretMutation(state.http.credential)
    case 'Kafka':
      return hasSecretMutation(state.kafka.password)
    case 'Nats':
      return hasSecretMutation(state.nats.token) || hasSecretMutation(state.nats.password)
    case 'RabbitMq':
      return hasSecretMutation(state.rabbitMq.password)
    case 'Sqs':
      return (
        hasSecretMutation(state.sqs.secretAccessKey) ||
        hasSecretMutation(state.sqs.sessionToken)
      )
    default:
      return false
  }
}

function parseDestinationConfiguration(
  destinationType: DestinationType,
  configurationText: string,
): DestinationFormState {
  const base = createDefaultDestinationFormState(destinationType)

  try {
    const parsed = JSON.parse(configurationText) as {
      type?: string
      url?: string
      authentication?: {
        type?: string
        credential?: {
          kind?: string
          value?: string
          version?: number
        }
      }
      bootstrapServers?: string
      topic?: string
      clientId?: string
      acks?: string
      timeoutSeconds?: number
      credentials?: {
        securityProtocol?: string
        saslMechanism?: string
        username?: string
        password?: {
          kind?: string
          value?: string
          version?: number
        }
        token?: {
          kind?: string
          value?: string
          version?: number
        }
        secretAccessKey?: {
          kind?: string
          value?: string
          version?: number
        }
        sessionToken?: {
          kind?: string
          value?: string
          version?: number
        }
        accessKeyId?: string
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

    const resolvedType: 'http' | 'kafka' | 'nats' | 'rabbitmq' | 'sqs' =
      parsed.type === 'http' ||
      parsed.type === 'kafka' ||
      parsed.type === 'nats' ||
      parsed.type === 'rabbitmq' ||
      parsed.type === 'sqs'
        ? parsed.type
        : (destinationType.toLowerCase() as 'http' | 'kafka' | 'nats' | 'rabbitmq' | 'sqs')

    if (resolvedType === 'http') {
      return {
        ...base,
        type: 'Http',
        http: {
          url: parsed.url ?? '',
          authenticationType:
            parsed.authentication?.type === 'bearer' ||
            parsed.authentication?.type === 'hmac'
              ? (parsed.authentication.type as 'bearer' | 'hmac')
              : 'none',
          credential: parsed.authentication?.credential
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind:
                  parsed.authentication.credential.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.authentication.credential.value ?? '',
                version: parsed.authentication.credential.version
                  ? String(parsed.authentication.credential.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
        },
      }
    }

    if (resolvedType === 'kafka') {
      return {
        ...base,
        type: 'Kafka',
        kafka: {
          bootstrapServers: parsed.bootstrapServers ?? '',
          topic: parsed.topic ?? '',
          clientId: parsed.clientId ?? '',
          acks: parsed.acks ?? '',
          timeoutSeconds: parsed.timeoutSeconds ? String(parsed.timeoutSeconds) : '',
          securityProtocol: parsed.credentials?.securityProtocol ?? '',
          saslMechanism: parsed.credentials?.saslMechanism ?? '',
          username: parsed.credentials?.username ?? '',
          password: parsed.credentials?.password
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind: parsed.credentials.password.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.credentials.password.value ?? '',
                version: parsed.credentials.password.version
                  ? String(parsed.credentials.password.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
        },
      }
    }

    if (resolvedType === 'nats') {
      return {
        ...base,
        type: 'Nats',
        nats: {
          url: parsed.url ?? '',
          subject: parsed.subject ?? '',
          timeoutSeconds: parsed.timeoutSeconds ? String(parsed.timeoutSeconds) : '',
          useJetStream: Boolean(parsed.useJetStream),
          stream: parsed.stream ?? '',
          token: parsed.credentials?.token
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind: parsed.credentials.token.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.credentials.token.value ?? '',
                version: parsed.credentials.token.version
                  ? String(parsed.credentials.token.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
          username: parsed.credentials?.username ?? '',
          password: parsed.credentials?.password
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind: parsed.credentials.password.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.credentials.password.value ?? '',
                version: parsed.credentials.password.version
                  ? String(parsed.credentials.password.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
        },
      }
    }

    if (resolvedType === 'rabbitmq') {
      return {
        ...base,
        type: 'RabbitMq',
        rabbitMq: {
          uri: parsed.uri ?? '',
          exchange: parsed.exchange ?? '',
          routingKey: parsed.routingKey ?? '',
          persistent: parsed.persistent ?? true,
          mandatory: parsed.mandatory ?? false,
          timeoutSeconds: parsed.timeoutSeconds ? String(parsed.timeoutSeconds) : '',
          username: parsed.credentials?.username ?? '',
          password: parsed.credentials?.password
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind: parsed.credentials.password.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.credentials.password.value ?? '',
                version: parsed.credentials.password.version
                  ? String(parsed.credentials.password.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
        },
      }
    }

    if (resolvedType === 'sqs') {
      return {
        ...base,
        type: 'Sqs',
        sqs: {
          region: parsed.region ?? '',
          queueUrl: parsed.queueUrl ?? '',
          serviceUrl: parsed.serviceUrl ?? '',
          timeoutSeconds: parsed.timeoutSeconds ? String(parsed.timeoutSeconds) : '',
          accessKeyId: parsed.credentials?.accessKeyId ?? '',
          secretAccessKey: parsed.credentials?.secretAccessKey
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind:
                  parsed.credentials.secretAccessKey.kind === 'ref'
                    ? 'ref'
                    : 'inline',
                value: parsed.credentials.secretAccessKey.value ?? '',
                version: parsed.credentials.secretAccessKey.version
                  ? String(parsed.credentials.secretAccessKey.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
          sessionToken: parsed.credentials?.sessionToken
            ? {
                mode: 'keep',
                hasStoredValue: true,
                kind:
                  parsed.credentials.sessionToken.kind === 'ref' ? 'ref' : 'inline',
                value: parsed.credentials.sessionToken.value ?? '',
                version: parsed.credentials.sessionToken.version
                  ? String(parsed.credentials.sessionToken.version)
                  : '',
              }
            : createDefaultSecretFieldState(),
        },
      }
    }

    return base
  } catch {
    return base
  }
}

type ContractEditorFormProps = {
  id: string
  customerId: string
  etag: string
  initial: EditorInitialState
  canWriteContracts: boolean
}

function ContractEditorForm({
  id,
  customerId,
  etag,
  initial,
  canWriteContracts,
}: ContractEditorFormProps) {
  const navigate = useNavigate()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const replaceMutation = useReplaceContractMutation(id)

  const [name, setName] = useState(initial.name)
  const [eventTypesInput, setEventTypesInput] = useState(initial.eventTypesInput)
  const [destination, setDestination] = useState(initial.destination)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const eventTypes = useMemo(() => parseEventTypes(eventTypesInput), [eventTypesInput])

  const fieldErrors = useMemo(() => {
    const errors: Record<string, string> = {}

    if (eventTypes.length === 0) {
      errors.eventTypes = 'At least one event type is required.'
    }

    const destinationError = validateDestinationFormState(destination)
    if (destinationError) {
      errors.destination = destinationError
    }

    return errors
  }, [destination, eventTypes.length])

  const hasValidationError = Object.keys(fieldErrors).length > 0

  const dirty = useMemo(() => {
    const currentSnapshot = JSON.stringify({
      name: name.trim(),
      eventTypes,
      destination: mapDestinationFormToConfig(destination),
    })

    const initialSnapshot = JSON.stringify({
      name: initial.name.trim(),
      eventTypes: parseEventTypes(initial.eventTypesInput),
      destination: mapDestinationFormToConfig(initial.destination),
    })

    return currentSnapshot !== initialSnapshot
  }, [
    destination,
    eventTypes,
    initial.destination,
    initial.eventTypesInput,
    initial.name,
    name,
  ])

  useUnsavedChangesGuard({ when: dirty })

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteContracts || replaceMutation.isPending || hasValidationError) {
      return
    }

    if (hasCredentialRotationChange(destination)) {
      const confirmed = await confirm({
        title: 'Confirm credential rotation',
        description:
          'You are changing or removing destination credentials. This can interrupt delivery authentication until the destination is updated with matching credentials.',
        confirmText: 'Save credential changes',
        cancelText: 'Cancel',
        confirmColor: 'amber',
      })

      if (!confirmed) {
        return
      }
    }

    setSubmitError(null)

    try {
      const result = await replaceMutation.mutateAsync({
        etag,
        payload: {
          customerId,
          name: name.trim() || null,
          eventTypes,
          destination: mapDestinationFormToConfig(destination),
          url: '',
          authenticationType: 'none',
        },
      })

      notifications.success('Contract changes saved.', 'Save contract')
      navigate(`/contracts/${encodeURIComponent(result.contract.id)}`)
    } catch (error) {
      setSubmitError(getServerValidation(error, 'Unable to update contract.').formError)
      notifications.error(getApiErrorMessage(error, 'Unable to update contract.'), 'Save contract')
    }
  }

  async function handleCancelNavigation() {
    if (dirty) {
      const confirmed = await confirm({
        title: 'Unsaved changes',
        description: 'You have unsaved changes. Leave this page?',
        confirmText: 'Leave page',
        cancelText: 'Stay',
        confirmColor: 'amber',
      })

      if (!confirmed) {
        return
      }
    }

    navigate(`/contracts/${encodeURIComponent(id)}`)
  }

  return (
    <Card>
      <form onSubmit={handleSubmit}>
        <Flex direction="column" gap="3">
          <Text size="2" color="gray">
            Contract name
          </Text>
          <TextField.Root
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={!canWriteContracts || replaceMutation.isPending}
          />

          <Text size="2" color="gray">
            Event types
          </Text>
          <TextArea
            value={eventTypesInput}
            onChange={(event) => setEventTypesInput(event.target.value)}
            rows={4}
            disabled={!canWriteContracts || replaceMutation.isPending}
            aria-invalid={fieldErrors.eventTypes ? 'true' : 'false'}
            aria-describedby={fieldErrors.eventTypes ? 'contract-edit-event-types-error' : undefined}
          />
          <FieldErrorText id="contract-edit-event-types-error" message={fieldErrors.eventTypes} />

          <DestinationConfigurationForm
            value={destination}
            onChange={setDestination}
          />
          <FieldErrorText id="contract-edit-destination-error" message={fieldErrors.destination} />

          <Text size="2" color="gray">
            Filters and transformations editing will be added in a later iteration.
          </Text>

          <FormErrorText
            id="contract-edit-submit-error"
            message={
              submitError ??
              (replaceMutation.isError
                ? getApiErrorMessage(replaceMutation.error, 'Unable to update contract.')
                : null)
            }
          />

          <Flex gap="2">
            <Button
              type="submit"
              disabled={!canWriteContracts || replaceMutation.isPending || hasValidationError || !dirty}
            >
              {replaceMutation.isPending ? 'Saving...' : 'Save changes'}
            </Button>

            <Button
              type="button"
              variant="soft"
              onClick={() => {
                void handleCancelNavigation()
              }}
            >
              Cancel
            </Button>
          </Flex>
        </Flex>
      </form>
    </Card>
  )
}

export function ContractEditPage() {
  const { id } = useParams()
  const { currentUser } = useAppSelector((state) => state.auth)
  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')

  const detailQuery = useContractDetailQuery(id)

  if (!canReadContracts) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Edit contract</Heading>
          <Text color="gray">You do not have permission to read contracts.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  if (!id) {
    return (
      <Card>
        <Text color="red">Invalid contract id.</Text>
      </Card>
    )
  }

  if (detailQuery.isLoading) {
    return (
      <Card>
        <Text size="2" color="gray">Loading contract editor...</Text>
      </Card>
    )
  }

  if (detailQuery.isError || !detailQuery.data?.contract || !detailQuery.data.etag) {
    return (
      <Card>
        <Text size="2" color="red">
          {getApiErrorMessage(detailQuery.error, 'Unable to load contract for editing.')}
        </Text>
      </Card>
    )
  }

  const contract = detailQuery.data.contract
  const destination = parseDestinationConfiguration(
    contract.destinationType,
    contract.destinationConfiguration,
  )

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Edit contract</Heading>
      <ContractEditorForm
        key={`${contract.id}-${contract.updatedAt}`}
        id={contract.id}
        customerId={contract.customerId}
        etag={detailQuery.data.etag}
        canWriteContracts={canWriteContracts}
        initial={{
          name: contract.name ?? '',
          eventTypesInput: contract.eventTypes.join(', '),
          destination,
        }}
      />
    </Flex>
  )
}
