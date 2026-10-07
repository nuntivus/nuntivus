import { Badge, Button, Card, Flex, Heading, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink, useParams, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { EventsIcon } from '../components/icons/nav-icons'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import { normalizeConfiguration } from '../features/contracts/destination-forms/normalize-configuration'
import { hydrateDestinationForm, mergeDestinationConfiguration } from '../features/contracts/destination-forms/stored-configuration'
import { normalizeDestinationType, validateDestinationFormState, type DestinationFormState } from '../features/contracts/destination-forms/types'
import { useDestinationDetailQuery, useUpdateDestinationMutation } from '../features/destinations/use-destinations'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DestinationResource } from '../services/api/destinations-api'
import { useAppSelector } from '../store/hooks'

function ConfigurationDetails({ item }: { item: DestinationResource }) {
  if (!item.configured) return <Card><Flex direction="column" gap="2"><Heading size="4">Configuration needed</Heading>
    <Text size="2" color="gray">Configure this destination before it can be enabled or linked to contracts.</Text>
  </Flex></Card>
  const type = normalizeDestinationType(item.type)
  if (!type) return <Text color="red">Unsupported destination type.</Text>
  let config: Record<string, unknown>
  try {
    config = normalizeConfiguration(JSON.parse(item.configuration)) as Record<string, unknown>
    if (!config || typeof config !== 'object') throw new Error('Invalid configuration')
  } catch {
    return <Text color="red">Unable to read destination configuration.</Text>
  }
  const credentials = config.credentials as Record<string, unknown> | undefined
  const auth = config.authentication as Record<string, unknown> | undefined
  const rows: [string, unknown][] = type === 'Http' ? [
    ['Destination URL', config.url], ['Authentication', auth?.type ?? 'none'],
    ['Credential', auth?.credential ? 'Configured (protected)' : 'None'],
    ['Headers', config.headers ? Object.keys(config.headers as object).join(', ') || 'None' : 'None'],
    ['Protected headers', config.secretHeaders ? Object.keys(config.secretHeaders as object).join(', ') || 'None' : 'None'],
    ['Timeout (seconds)', config.timeoutSeconds],
  ] : type === 'Kafka' ? [
    ['Bootstrap servers', config.bootstrapServers], ['Topic', config.topic], ['Client ID', config.clientId],
    ['Acknowledgements', config.acks], ['Security protocol', credentials?.securityProtocol],
    ['SASL mechanism', credentials?.saslMechanism], ['Username', credentials?.username],
    ['Password', credentials?.password ? 'Configured (protected)' : 'None'], ['Timeout (seconds)', config.timeoutSeconds],
  ] : type === 'Nats' ? [
    ['URL', config.url], ['Subject', config.subject], ['JetStream', config.useJetStream ? 'Enabled' : 'Disabled'],
    ['Stream', config.stream], ['Username', credentials?.username],
    ['Token', credentials?.token ? 'Configured (protected)' : 'None'],
    ['Password', credentials?.password ? 'Configured (protected)' : 'None'], ['Timeout (seconds)', config.timeoutSeconds],
  ] : type === 'RabbitMq' ? [
    ['URI', config.uri], ['Exchange', config.exchange], ['Routing key', config.routingKey],
    ['Persistent', config.persistent ? 'Yes' : 'No'], ['Mandatory', config.mandatory ? 'Yes' : 'No'],
    ['Username', credentials?.username], ['Password', credentials?.password ? 'Configured (protected)' : 'None'],
    ['Timeout (seconds)', config.timeoutSeconds],
  ] : [
    ['Region', config.region], ['Queue URL', config.queueUrl], ['Service URL', config.serviceUrl],
    ['Access key ID', credentials?.accessKeyId ? 'Configured' : 'None'],
    ['Secret access key', credentials?.secretAccessKey ? 'Configured (protected)' : 'None'],
    ['Session token', credentials?.sessionToken ? 'Configured (protected)' : 'None'],
    ['Timeout (seconds)', config.timeoutSeconds],
  ]
  return <Card><Flex direction="column" gap="3"><Heading size="4">{type} configuration</Heading>
    {rows.map(([label, value]) => <Text key={label} size="2"><strong>{label}:</strong> {value == null || value === '' ? '—' : String(value)}</Text>)}
  </Flex></Card>
}

function DestinationEditor({ item, onCancel }: { item: DestinationResource; onCancel: () => void }) {
  const type = normalizeDestinationType(item.type)
  const update = useUpdateDestinationMutation()
  const notifications = useNotifications()
  const [name, setName] = useState(item.name)
  const [description, setDescription] = useState(item.description ?? '')
  const [initialForm] = useState<DestinationFormState | null>(() => {
    if (!type) return null
    try { return hydrateDestinationForm(type, item.configuration) } catch { return null }
  })
  const [form, setForm] = useState(initialForm)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const dirty = name !== item.name || description !== (item.description ?? '') ||
    JSON.stringify(form) !== JSON.stringify(initialForm)
  useUnsavedChangesGuard({ when: dirty && !saved })

  if (!form) return <Text color="red">Unable to edit this destination configuration.</Text>

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!form || update.isPending) return
    const validation = !name.trim() ? 'Name is required.' : validateDestinationFormState(form)
    if (validation) { setError(validation); return }
    setError(null)
    try {
      await update.mutateAsync({ id: item.id, input: {
        name: name.trim(), description: description.trim() || null,
        configuration: mergeDestinationConfiguration(item.configuration, form), rawConfiguration: true,
      } })
      notifications.success('Destination saved.', 'Destinations')
      setSaved(true)
      onCancel()
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to save Destination.')) }
  }

  return <Card><form onSubmit={(event) => void submit(event)}><Flex direction="column" gap="3">
    <Heading size="4">{item.configured ? 'Edit' : 'Configure'} {type} destination</Heading>
    <Text size="2" color="gray">Changes here affect new links or contracts that explicitly adopt the latest settings. Stored credentials stay protected.</Text>
    <Text as="label" size="2">Name<TextField.Root value={name} onChange={event => setName(event.target.value)} required /></Text>
    <Text as="label" size="2">Description<TextField.Root value={description} onChange={event => setDescription(event.target.value)} /></Text>
    <DestinationConfigurationForm fixedType value={form} onChange={setForm} />
    {error && <Text color="red">{error}</Text>}
    <Flex gap="2"><Button type="submit" disabled={update.isPending}>{item.configured ? 'Save changes' : 'Save configuration'}</Button><Button type="button" variant="soft" onClick={onCancel}>Cancel</Button></Flex>
  </Flex></form></Card>
}

export function DestinationDetailPage() {
  const { id } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector(state => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const query = useDestinationDetailQuery(id)
  const [editingId, setEditingId] = useState<string | null>(null)
  const editing = canWrite && (editingId === id || searchParams.get('edit') === '1')
  const closeEditor = () => { setEditingId(null); if (searchParams.has('edit')) setSearchParams(previous => { const next = new URLSearchParams(previous); next.delete('edit'); return next }, { replace: true }) }
  if (!id) return <Card><Text color="red">Invalid Destination ID.</Text></Card>
  if (query.isLoading) return <DetailLoadingState message="Loading Destination..." />
  if (query.isError || !query.data) return <Card><Flex direction="column" gap="3">
    <Text color={query.error instanceof ApiError && query.error.status === 404 ? 'gray' : 'red'}>{getApiErrorMessage(query.error, 'Destination not found.')}</Text>
    <Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>
  </Flex></Card>
  const item = query.data
  return <Flex direction="column" gap="4" className="detail-page destination-detail-page">
    <DetailPageHeader title={item.name} description="Shared destination configuration and contract usage." icon={EventsIcon}
      actions={<Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>} />
    <Card><Flex direction="column" gap="2"><Heading size="4">Overview</Heading>
      <Text size="2"><strong>Destination ID:</strong> {item.id}</Text>
      <Text size="2"><strong>Type:</strong> {normalizeDestinationType(item.type) ?? String(item.type)}</Text>
      <Text size="2"><strong>Status:</strong> <Badge color={item.configured ? item.enabled ? 'green' : 'gray' : 'amber'}>{item.configured ? item.enabled ? 'Enabled' : 'Disabled' : 'Needs configuration'}</Badge></Text>
      <Text size="2"><strong>Description:</strong> {item.description || '—'}</Text>
      <Text size="2"><strong>Created:</strong> {new Date(item.createdAt).toLocaleString()}</Text>
      <Text size="2"><strong>Updated:</strong> {new Date(item.updatedAt).toLocaleString()}</Text>
      <Text size="2">Edits here apply to new links or explicitly refreshed contract snapshots.</Text>
    </Flex></Card>
    {canWrite && !editing && <Button style={{ alignSelf: 'start' }} onClick={() => setEditingId(item.id)}>{item.configured ? 'Edit destination' : 'Configure destination'}</Button>}
    {editing ? <DestinationEditor key={`${item.id}-${item.updatedAt}`} item={item} onCancel={closeEditor} /> : <ConfigurationDetails item={item} />}
  </Flex>
}
