import { Badge, Button, Card, Flex, Heading, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink, useParams, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { EventsIcon, OverviewIcon } from '../components/icons/nav-icons'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import { normalizeConfiguration } from '../features/contracts/destination-forms/normalize-configuration'
import { hydrateDestinationForm, mergeDestinationConfiguration } from '../features/contracts/destination-forms/stored-configuration'
import { normalizeDestinationType, validateDestinationFormState, type DestinationFormState } from '../features/contracts/destination-forms/types'
import { useDestinationDetailQuery, useUpdateDestinationMutation } from '../features/destinations/use-destinations'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { testDestinationConnection, type DestinationResource } from '../services/api/destinations-api'
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
    ['Destination URL', config.url], ['Method', config.method ?? 'POST'], ['Authentication', auth?.type ?? 'none'],
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
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ form: string; message: string; failed: boolean } | null>(null)
  const [saved, setSaved] = useState(false)
  const dirty = name !== item.name || description !== (item.description ?? '') ||
    JSON.stringify(form) !== JSON.stringify(initialForm)
  useUnsavedChangesGuard({ when: dirty && !saved })

  if (!form) return <Text color="red">Unable to edit this destination configuration.</Text>

  async function testConnection() {
    if (!form) return
    const snapshot = JSON.stringify(form)
    setTesting(true)
    setTestResult(null)
    try {
      const result = await testDestinationConnection(mergeDestinationConfiguration(item.configuration, form), item.id)
      setTestResult({ form: snapshot, message: result.message, failed: false })
    } catch (cause) {
      setTestResult({ form: snapshot, message: getApiErrorMessage(cause, 'Connection test failed.'), failed: true })
    } finally { setTesting(false) }
  }

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

  return <Card className="settings-editor-card source-configuration-card"><form onSubmit={(event) => void submit(event)} className="settings-editor-form">
    <div className="source-configuration-heading"><span className="source-configuration-icon"><EventsIcon /></span><div>
      <Heading size="4">{type} Destination Configuration</Heading>
      <Text size="2" color="gray">Changes here affect new links or contracts that explicitly adopt the latest settings. Stored credentials stay protected.</Text>
    </div></div>
    <div className="settings-editor-fields">
      <label className="settings-editor-field"><span>Name <span aria-hidden="true">*</span></span><TextField.Root value={name} onChange={event => setName(event.target.value)} required />
        <Text size="1" color="gray">A recognizable name for this Destination.</Text></label>
      <label className="settings-editor-field">Type<TextField.Root value={type ?? ''} readOnly /></label>
      <label className="settings-editor-field source-description-field">Description<TextArea value={description} onChange={event => setDescription(event.target.value)} />
        <Text size="1" color="gray">Optional context for operators.</Text></label>
    </div>
    <details className="source-advanced-settings"><summary>Advanced Settings</summary>
      <DestinationConfigurationForm fixedType value={form} onChange={setForm} />
    </details>
    {error && <Text color="red">{error}</Text>}
    {testResult?.form === JSON.stringify(form) && <Text color={testResult.failed ? 'red' : 'green'} role="status">{testResult.message}</Text>}
    <div className="settings-editor-actions"><Button type="submit" disabled={update.isPending}>{item.configured ? 'Save changes' : 'Save configuration'}</Button><Button type="button" variant="soft" onClick={() => void testConnection()} disabled={testing || update.isPending}>{testing ? 'Testing...' : 'Test connection'}</Button><Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button></div>
  </form></Card>
}

export function DestinationDetailPage() {
  const { id } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const { currentUser } = useAppSelector(state => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const query = useDestinationDetailQuery(id)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [selectedTab, setSelectedTab] = useState<'overview' | 'configuration'>(
    searchParams.get('edit') === '1' && canWrite ? 'configuration' : 'overview')
  const editing = canWrite && (editingId === id || searchParams.get('edit') === '1')
  const closeEditor = () => {
    setEditingId(null)
    setSelectedTab('overview')
    if (searchParams.has('edit')) setSearchParams(previous => { const next = new URLSearchParams(previous); next.delete('edit'); return next }, { replace: true })
  }
  if (!id) return <Card><Text color="red">Invalid Destination ID.</Text></Card>
  if (query.isLoading) return <DetailLoadingState message="Loading Destination..." />
  if (query.isError || !query.data) return <Card><Flex direction="column" gap="3">
    <Text color={query.error instanceof ApiError && query.error.status === 404 ? 'gray' : 'red'}>{getApiErrorMessage(query.error, 'Destination not found.')}</Text>
    <Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>
  </Flex></Card>
  const item = query.data
  const status = item.configured ? item.enabled ? 'Enabled' : 'Disabled' : 'Needs configuration'
  const statusColor = item.configured ? item.enabled ? 'green' : 'gray' : 'amber'
  return <div className="source-detail-frame">
    <div className="source-detail-top">
      <DetailPageHeader title={item.name} description="Shared destination configuration and contract usage." icon={EventsIcon}
        actions={<><Badge color={statusColor}>{status}</Badge><Button asChild variant="soft"><NavLink to="/destinations">← Back to Destinations</NavLink></Button></>} />
      <div className="source-detail-tabs" role="tablist" aria-label="Destination sections">
        <button type="button" role="tab" id="destination-tab-overview" aria-controls="destination-panel-overview" aria-selected={selectedTab === 'overview'} onClick={() => setSelectedTab('overview')}>Overview</button>
        <button type="button" role="tab" id="destination-tab-configuration" aria-controls="destination-panel-configuration" aria-selected={selectedTab === 'configuration'} onClick={() => {
          if (canWrite) setEditingId(item.id)
          setSelectedTab('configuration')
        }}>Configuration</button>
      </div>
    </div>
    <div id="destination-panel-overview" role="tabpanel" aria-labelledby="destination-tab-overview" className="source-detail-content" hidden={selectedTab !== 'overview'}>
      <Card className="source-overview-card">
        <Heading size="4" className="source-section-title"><OverviewIcon /> Destination Overview</Heading>
        <div className="source-overview-fields">
          <div><Text size="2" color="gray">Destination ID</Text><Text size="2">{item.id}</Text></div>
          <div><Text size="2" color="gray">Type</Text><Text size="2">{normalizeDestinationType(item.type) ?? String(item.type)}</Text></div>
          <div><Text size="2" color="gray">Status</Text><Badge color={statusColor}>{status}</Badge></div>
          <div><Text size="2" color="gray">Description</Text><Text size="2">{item.description || '—'}</Text></div>
        </div>
        <div className="source-overview-dates">
          <div><Text size="2" color="gray">Created</Text><Text size="2">{new Date(item.createdAt).toLocaleString()}</Text></div>
          <div><Text size="2" color="gray">Last updated</Text><Text size="2">{new Date(item.updatedAt).toLocaleString()}</Text></div>
        </div>
        <Text as="p" size="2" color="gray">Edits here apply to new links or explicitly refreshed contract snapshots.</Text>
      </Card>
      {canWrite && <Button style={{ alignSelf: 'start' }} onClick={() => { setEditingId(item.id); setSelectedTab('configuration') }}>{item.configured ? 'Edit destination' : 'Configure destination'}</Button>}
    </div>
    <div id="destination-panel-configuration" role="tabpanel" aria-labelledby="destination-tab-configuration" className="source-detail-content" hidden={selectedTab !== 'configuration'}>
      {editing ? <DestinationEditor key={`${item.id}-${item.updatedAt}`} item={item} onCancel={closeEditor} /> : <ConfigurationDetails item={item} />}
    </div>
  </div>
}
