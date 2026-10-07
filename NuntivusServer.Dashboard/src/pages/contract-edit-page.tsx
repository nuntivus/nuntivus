import {
  Button,
  Card,
  Flex,
  Heading,
  Switch,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { getServerValidation } from '../components/forms/form-validation'
import { ContractsIcon } from '../components/icons/nav-icons'
import { ContractOptionsForm } from '../features/contracts/contract-options-form'
import { ContractSourceSelector } from '../features/contracts/contract-source-selector'
import { ContractDestinationSelector } from '../features/contracts/contract-destination-selector'
import { backendConfigToFormState, formStateToBackendConfig, validateAdvancedContractOptions } from '../features/contracts/contract-options'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import { normalizeConfiguration } from '../features/contracts/destination-forms/normalize-configuration'
import {
  createDefaultSecretFieldState,
  createDefaultDestinationFormState,
  mapDestinationFormToConfig,
  normalizeDestinationType,
  type DestinationFormState,
  validateDestinationFormState,
} from '../features/contracts/destination-forms/types'
import {
  useContractDetailQuery,
  useDisableContractMutation,
  useEnableContractMutation,
  useReplaceContractMutation,
} from '../features/contracts/use-contracts'
import { getApiErrorMessage } from '../services/api/api-client'
import type { ContractOptions, DestinationType } from '../services/api/contracts-api'
import { useAppSelector } from '../store/hooks'

type EditorInitialState = {
  name: string
  sourceIds: string[]
  eventTypesInput: string
  enabled: boolean
  destination: DestinationFormState
  destinationConfiguration: string
  destinationIds: string[]
  detached: { id: string; type: string }[]
  options: ContractOptions
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
    const parsed = normalizeConfiguration(JSON.parse(configurationText)) as {
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
  const enableMutation = useEnableContractMutation()
  const disableMutation = useDisableContractMutation()

  const [name, setName] = useState(initial.name)
  const [sourceIds, setSourceIds] = useState(initial.sourceIds)
  const [eventTypesInput, setEventTypesInput] = useState(initial.eventTypesInput)
  const [enabled, setEnabled] = useState(initial.enabled)
  const [destination, setDestination] = useState(initial.destination)
  const [destinationIds, setDestinationIds] = useState(initial.destinationIds)
  const [refreshDestinationIds, setRefreshDestinationIds] = useState<string[]>([])
  const [retainedDetachedIds, setRetainedDetachedIds] = useState(initial.detached.map(binding => binding.id))
  const [optionsDraft, setOptionsDraft] = useState(() => backendConfigToFormState(initial.options))
  const [rawEditing, setRawEditing] = useState<string[]>([])
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [savedContractId, setSavedContractId] = useState<string | null>(null)

  const eventTypes = useMemo(() => parseEventTypes(eventTypesInput), [eventTypesInput])
  const optionErrors = useMemo(() => validateAdvancedContractOptions(optionsDraft, eventTypes), [optionsDraft, eventTypes])

  const fieldErrors = useMemo(() => {
    const errors: Record<string, string> = {}

    if (eventTypes.length === 0) {
      errors.eventTypes = 'At least one event type is required.'
    }

    if (sourceIds.length === 0) {
      errors.sources = 'Select at least one Source.'
    }

    if (Object.keys(optionErrors).length) {
      errors.options = 'Correct the advanced contract options before saving.'
    }

    const destinationError = destinationIds.length || retainedDetachedIds.length
      ? null : initial.destinationIds.length ? 'Select at least one Destination.' : validateDestinationFormState(destination)
    if (destinationError) {
      errors.destination = destinationError
    }

    return errors
  }, [destination, destinationIds, retainedDetachedIds, initial.destinationIds.length, eventTypes.length, optionErrors, sourceIds])

  const hasValidationError = Object.keys(fieldErrors).length > 0

  const dirty = useMemo(() => {
    const currentSnapshot = JSON.stringify({
      name: name.trim(),
      sourceIds,
      eventTypes,
      enabled,
      destination: mapDestinationFormToConfig(destination),
      destinationIds,
      refreshDestinationIds,
      retainedDetachedIds,
      options: optionsDraft,
    })

    const initialSnapshot = JSON.stringify({
      name: initial.name.trim(),
      sourceIds: initial.sourceIds,
      eventTypes: parseEventTypes(initial.eventTypesInput),
      enabled: initial.enabled,
      destination: mapDestinationFormToConfig(initial.destination),
      destinationIds: initial.destinationIds,
      refreshDestinationIds: [],
      retainedDetachedIds: initial.detached.map(binding => binding.id),
      options: backendConfigToFormState(initial.options),
    })

    return currentSnapshot !== initialSnapshot
  }, [
    destination,
    destinationIds,
    refreshDestinationIds,
    retainedDetachedIds,
    initial.detached,
    initial.destinationIds,
    enabled,
    eventTypes,
    initial.destination,
    initial.enabled,
    initial.eventTypesInput,
    initial.name,
    initial.sourceIds,
    initial.options,
    name,
    sourceIds,
    optionsDraft,
  ])

  useUnsavedChangesGuard({ when: !savedContractId && (dirty || rawEditing.length > 0) })

  useEffect(() => {
    if (savedContractId) {
      navigate(`/contracts/${encodeURIComponent(savedContractId)}`)
    }
  }, [navigate, savedContractId])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteContracts || replaceMutation.isPending || enableMutation.isPending || disableMutation.isPending || hasValidationError || rawEditing.length > 0) {
      return
    }

    if (!destinationIds.length && hasCredentialRotationChange(destination)) {
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
      const destinationConfig = mapDestinationFormToConfig(destination)
      const storedDestination = normalizeConfiguration(JSON.parse(initial.destinationConfiguration)) as Record<string, unknown>
      const mergedDestination = destinationConfig.type === initial.destination.type.toLowerCase()
        ? {
          ...storedDestination,
          ...destinationConfig,
          ...('credentials' in destinationConfig && destinationConfig.credentials
            ? { credentials: { ...(storedDestination.credentials as Record<string, unknown> ?? {}), ...destinationConfig.credentials } }
            : {}),
          ...(destinationConfig.type === 'http'
            ? {
              headers: storedDestination.headers,
              timeoutSeconds: storedDestination.timeoutSeconds,
            }
            : {}),
        }
        : destinationConfig
      if (destinationConfig.type === 'http' && mergedDestination.type === 'http') {
        delete (mergedDestination as Record<string, unknown>).secretHeaders
      }
      const destinationPayload = destinationConfig.type === 'http'
        ? { ...mergedDestination, authenticationType: destinationConfig.authenticationType, credential: destinationConfig.credential }
        : mergedDestination
      const result = await replaceMutation.mutateAsync({
        etag,
        payload: {
          customerId,
          sourceIds,
          name: name.trim() || null,
          eventTypes,
          destination: destinationPayload as typeof destinationConfig,
          destinationIds: destinationIds.length || initial.destinationIds.length ? destinationIds : undefined,
          refreshDestinationIds: refreshDestinationIds.length ? refreshDestinationIds : undefined,
          removeDestinationBindingIds: initial.detached.filter(binding => !retainedDetachedIds.includes(binding.id)).map(binding => binding.id),
          ...formStateToBackendConfig(optionsDraft),
          url: '',
          authenticationType: 'none',
        },
      })

      if (enabled !== initial.enabled) {
        await (enabled ? enableMutation.mutateAsync(id) : disableMutation.mutateAsync(id))
      }

      notifications.success('Contract changes saved.', 'Save contract')
      setSavedContractId(result.contract.id)
    } catch (error) {
      setSubmitError(getServerValidation(error, 'Unable to update contract.').formError)
      notifications.error(getApiErrorMessage(error, 'Unable to update contract.'), 'Save contract')
    }
  }

  return (
    <form id="contract-edit-form" onSubmit={handleSubmit} className="contract-edit-form">
      <Card className="contract-edit-details-card">
        <Flex align="center" gap="2" className="contract-edit-card-title">
          <span><ContractsIcon /></span>
          <div><Heading size="4">Contract details</Heading><Text size="1" color="gray">Basic information about this contract.</Text></div>
        </Flex>
        <div className="contract-edit-details-grid">
          <label className="contract-edit-field">
            <Text size="2" weight="medium">Contract name <span aria-hidden="true">*</span></Text>
            <TextField.Root
              value={name}
              onChange={(event) => setName(event.target.value)}
              disabled={!canWriteContracts || replaceMutation.isPending}
            />
          </label>
          <label className="contract-edit-field">
            <Text size="2" weight="medium">Customer <span aria-hidden="true">*</span></Text>
            <TextField.Root value={customerId} readOnly disabled />
          </label>
          <div className="contract-edit-field">
            <ContractSourceSelector value={sourceIds} onChange={setSourceIds} disabled={!canWriteContracts || replaceMutation.isPending} />
            <FieldErrorText id="contract-edit-sources-error" message={fieldErrors.sources} />
          </div>
          <label className="contract-edit-field">
            <Text size="2" weight="medium">Event types <span aria-hidden="true">*</span></Text>
            <TextArea
              value={eventTypesInput}
              onChange={(event) => setEventTypesInput(event.target.value)}
              rows={1}
              disabled={!canWriteContracts || replaceMutation.isPending}
              aria-invalid={fieldErrors.eventTypes ? 'true' : 'false'}
              aria-describedby={fieldErrors.eventTypes ? 'contract-edit-event-types-error' : undefined}
            />
            <FieldErrorText id="contract-edit-event-types-error" message={fieldErrors.eventTypes} />
          </label>
          <label className="contract-edit-enabled-field">
            <Text size="2" weight="medium">Enabled</Text>
            <Switch checked={enabled} onCheckedChange={setEnabled} disabled={!canWriteContracts || replaceMutation.isPending || enableMutation.isPending || disableMutation.isPending} aria-label="Enabled" />
            <Text size="1" color="gray">Enable or disable this contract.</Text>
          </label>
          <div className="contract-edit-destination-fields">
            <ContractDestinationSelector value={destinationIds} onChange={setDestinationIds}
              refreshIds={refreshDestinationIds} onRefreshChange={setRefreshDestinationIds}
              detached={initial.detached} retainedDetachedIds={retainedDetachedIds}
              onRetainedDetachedChange={setRetainedDetachedIds}
              disabled={!canWriteContracts || replaceMutation.isPending} />
            {!destinationIds.length && !initial.destinationIds.length && <DestinationConfigurationForm value={destination} onChange={setDestination} />}
          </div>
          <FieldErrorText id="contract-edit-destination-error" message={fieldErrors.destination} />
        </div>
      </Card>

      <ContractOptionsForm standalone value={optionsDraft} onChange={setOptionsDraft} eventTypes={eventTypes} disabled={!canWriteContracts || replaceMutation.isPending} onRawEditingChange={(key, editing) => setRawEditing((current) => editing ? [...current.filter((item) => item !== key), key] : current.filter((item) => item !== key))} />
      <FieldErrorText id="contract-edit-options-error" message={fieldErrors.options} />
      <FormErrorText id="contract-edit-submit-error" message={submitError ?? (replaceMutation.isError ? getApiErrorMessage(replaceMutation.error, 'Unable to update contract.') : null)} />
      <input type="hidden" value={String(enabled)} readOnly />
    </form>
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
          {detailQuery.isError
            ? getApiErrorMessage(detailQuery.error, 'Unable to load contract for editing.')
            : 'The contract ETag is unavailable. Check that the API exposes the ETag response header.'}
        </Text>
      </Card>
    )
  }

  const contract = detailQuery.data.contract
  const destinationType = normalizeDestinationType(contract.destinationType)
  if (!destinationType) {
    return (
      <Card>
        <Text size="2" color="red">This contract has an unsupported destination type.</Text>
      </Card>
    )
  }

  const destination = parseDestinationConfiguration(
    destinationType,
    contract.destinationConfiguration,
  )

  return (
    <Flex direction="column" gap="4" className="contract-edit-page">
      <Flex align="center" justify="between" gap="3" wrap="wrap" className="contract-edit-header">
        <Flex align="center" gap="3">
          <span className="contract-edit-header-icon"><ContractsIcon /></span>
          <div><Heading size="6">Edit contract</Heading><Text size="2" color="gray">Update contract configuration and settings for event delivery.</Text></div>
        </Flex>
        <Flex gap="2">
          <Button asChild variant="soft">
            <NavLink to={`/contracts/${encodeURIComponent(contract.id)}`}>Cancel</NavLink>
          </Button>
          <Button type="submit" form="contract-edit-form" disabled={!canWriteContracts}>Save changes</Button>
        </Flex>
      </Flex>
      <ContractEditorForm
        key={`${contract.id}-${contract.updatedAt}`}
        id={contract.id}
        customerId={contract.customerId}
        etag={detailQuery.data.etag}
        canWriteContracts={canWriteContracts}
        initial={{
          name: contract.name ?? '',
          sourceIds: contract.sourceIds,
          eventTypesInput: contract.eventTypes.join(', '),
          enabled: contract.enabled,
          destination,
          destinationConfiguration: contract.destinationConfiguration,
          destinationIds: contract.destinations?.flatMap(binding => binding.destinationId ? [binding.destinationId] : []) ?? [],
          detached: contract.destinations?.filter(binding => !binding.destinationId).map(binding => ({ id: binding.id, type: String(binding.type) })) ?? [],
          options: {
            schemaTargets: contract.schemaTargets,
            filter: contract.filter,
            transformation: contract.transformation,
            rateLimit: contract.rateLimit,
            ordering: contract.ordering,
          },
        }}
      />
    </Flex>
  )
}
