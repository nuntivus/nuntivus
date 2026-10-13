import { Badge, Button, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { NavLink, useParams, useSearchParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { frontendConfig } from '../app/frontend-config'
import { CopyIcon, EventsIcon, OverviewIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { useSourceDetailQuery } from '../features/sources/use-sources'
import { SourceForm } from '../features/sources/source-form'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { Source } from '../services/api/sources-api'
import { sourceFields } from '../features/sources/source-types'
import { useAppSelector } from '../store/hooks'

function HttpSourceDetails({ source }: { source: Source }) {
  const notifications = useNotifications()
  const endpoint = `${frontendConfig.apiBaseUrl}/v1/sources/${source.id}/events`
  const tokenRequired = source.configuration.authentication?.type === 'token'
  const method = typeof source.configuration.method === 'string' ? source.configuration.method : 'POST'
  const example = [
    `curl -X ${method} '${endpoint}' \\`,
    "  -H 'Content-Type: application/json' \\",
    ...(tokenRequired ? ["  -H 'X-Nuntivus-Source-Token: <YOUR_TOKEN_VALUE>' \\"] : []),
    "  -d '{\"idempotencyKey\":\"example-001\",\"type\":\"orders.created\",\"payload\":{\"orderId\":\"123\"}}'",
  ].join('\n')

  async function copy(value: string, label: string) {
    try {
      await navigator.clipboard.writeText(value)
      notifications.success(`${label} copied.`, 'Sources')
    } catch {
      notifications.error(`Unable to copy ${label.toLowerCase()}.`, 'Sources')
    }
  }

  return <Flex direction="column" gap="4">
    <Card><Flex direction="column" gap="3">
      <Heading size="4">HTTP ingestion</Heading>
      <Text size="2" color="gray">Send {method} requests to this Source’s unique endpoint. The Source must be enabled to accept events.</Text>
      <Text size="2" weight="medium">Listening endpoint</Text>
      <Flex gap="2" align="center" wrap="wrap">
        <Text size="2" style={{ overflowWrap: 'anywhere' }}>{endpoint}</Text>
        <Button size="1" variant="soft" onClick={() => void copy(endpoint, 'Endpoint')}><CopyIcon /> Copy endpoint</Button>
      </Flex>
      <Text size="2">Authentication: {tokenRequired ? 'Token (X-Nuntivus-Source-Token header)' : 'None'}</Text>
      {tokenRequired && <Text size="2">Managed secret reference: {source.configuration.authentication?.secretRef ?? 'Not configured'}. Send the secret’s value as the token, not its reference name. The value is never displayed here.</Text>}
    </Flex></Card>
    <Card><Flex direction="column" gap="3">
      <Heading size="4">Example request (curl, Bash)</Heading>
      <Text size="2" color="gray">Replace the event type with one configured for your contracts, use a unique idempotency key per event{tokenRequired ? ', and replace the token placeholder with the value stored in Secrets' : ''}.</Text>
      <pre style={{ overflowX: 'auto', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', margin: 0 }}><code>{example}</code></pre>
      <Button size="1" variant="soft" style={{ alignSelf: 'start' }} onClick={() => void copy(example, 'Example request')}><CopyIcon /> Copy example</Button>
    </Flex></Card>
  </Flex>
}

export function SourceDetailPage() {
  const { id } = useParams()
  const notifications = useNotifications()
  const [searchParams, setSearchParams] = useSearchParams()
  const [editingId, setEditingId] = useState<string | null>(null)
  const [selectedTab, setSelectedTab] = useState<'overview' | 'configuration' | 'events'>(
    searchParams.get('edit') === '1' ? 'configuration' : 'overview')
  const { currentUser } = useAppSelector(state => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const query = useSourceDetailQuery(id)
  const editing = canWrite && (editingId === id || searchParams.get('edit') === '1')
  const activeTab = !canWrite && searchParams.get('edit') === '1' && selectedTab === 'configuration' ? 'overview' : selectedTab
  const closeEditor = () => {
    setEditingId(null)
    setSelectedTab('overview')
    if (searchParams.has('edit')) setSearchParams(previous => {
      const next = new URLSearchParams(previous)
      next.delete('edit')
      return next
    }, { replace: true })
  }

  if (!id) return <Card><Text color="red">Invalid Source ID.</Text></Card>
  if (query.isLoading) return <DetailLoadingState message="Loading Source detail..." />
  if (query.isError || !query.data) {
    const missing = query.error instanceof ApiError && query.error.status === 404
    return <Card><Flex direction="column" gap="3" align="start">
      <Heading size="5">Source detail</Heading>
      <Text color={missing ? 'gray' : 'red'}>{missing ? 'Source not found.' : getApiErrorMessage(query.error, 'Unable to load Source details.')}</Text>
      <Button asChild variant="soft"><NavLink to="/sources">Back to Sources</NavLink></Button>
    </Flex></Card>
  }

  const source = query.data
  function selectTab(tab: 'overview' | 'configuration' | 'events') {
    if (tab === 'configuration' && canWrite) setEditingId(source.id)
    setSelectedTab(tab)
  }

  return <div className="source-detail-frame">
    <div className="source-detail-top">
      <DetailPageHeader title={source.name} description="Source configuration and event ingestion details." icon={EventsIcon}
        actions={<><Badge color={source.enabled ? 'green' : 'gray'}>{source.enabled ? 'Enabled' : 'Disabled'}</Badge><Button asChild variant="soft"><NavLink to="/sources">← Back to Sources</NavLink></Button></>} />
      <div className="source-detail-tabs" role="tablist" aria-label="Source sections">
        <button type="button" role="tab" id="source-tab-overview" aria-controls="source-panel-overview" aria-selected={activeTab === 'overview'} onClick={() => selectTab('overview')}>Overview</button>
        <button type="button" role="tab" id="source-tab-configuration" aria-controls="source-panel-configuration" aria-selected={activeTab === 'configuration'} onClick={() => selectTab('configuration')}>Configuration</button>
        {source.type === 'HTTP_API' && <button type="button" role="tab" id="source-tab-events" aria-controls="source-panel-events" aria-selected={activeTab === 'events'} onClick={() => selectTab('events')}>Events</button>}
      </div>
    </div>
    <div id="source-panel-overview" role="tabpanel" aria-labelledby="source-tab-overview" className="source-detail-content" hidden={activeTab !== 'overview'}>
      <Card className="source-overview-card">
        <Heading size="4" className="source-section-title"><OverviewIcon /> Source Overview</Heading>
        <div className="source-overview-fields">
          <div><Text size="2" color="gray">Source ID</Text><Flex gap="2" align="center" wrap="wrap"><Text size="2" style={{ overflowWrap: 'anywhere' }}>{source.id}</Text><Button size="1" variant="ghost" aria-label="Copy Source ID" onClick={() => void navigator.clipboard.writeText(source.id).then(() => notifications.success('Source ID copied.', 'Sources')).catch(() => notifications.error('Unable to copy Source ID.', 'Sources'))}><CopyIcon /></Button></Flex></div>
          <div><Text size="2" color="gray">Type</Text><Text size="2">{source.type}</Text></div>
          <div><Text size="2" color="gray">Status</Text><Badge color={source.enabled ? 'green' : 'gray'}>{source.enabled ? 'Enabled' : 'Disabled'}</Badge></div>
          <div><Text size="2" color="gray">Description</Text><Text size="2">{source.description || '—'}</Text></div>
        </div>
        <div className="source-overview-dates">
          <div><Text size="2" color="gray">Created</Text><Text size="2">{new Date(source.createdAt).toLocaleString()}</Text></div>
          <div><Text size="2" color="gray">Last updated</Text><Text size="2">{new Date(source.updatedAt).toLocaleString()}</Text></div>
        </div>
      </Card>
      {canWrite && <Button style={{ alignSelf: 'start' }} onClick={() => selectTab('configuration')}>Edit source</Button>}
    </div>
    <div id="source-panel-configuration" role="tabpanel" aria-labelledby="source-tab-configuration" className="source-detail-content" hidden={activeTab !== 'configuration'}>
      {editing ? <SourceForm key={source.id} source={source} onSaved={closeEditor} onCancel={closeEditor} /> : <Card><Flex direction="column" gap="2">
        <Heading size="4">{source.type} configuration</Heading>
        <Text size="2">{source.type === 'HTTP_API' ? `Ingestion method: ${source.configuration.method ?? 'POST'}` : 'The Worker consumes events from this Source.'}</Text>
        {source.type === 'HTTP_API' && <Text size="2">Authentication: {source.configuration.authentication?.type ?? 'none'}</Text>}
      </Flex></Card>}
      {!editing && sourceFields[source.type] && <Card><Flex direction="column" gap="2">
      <Heading size="4">{source.type} consumer</Heading>
      <Text size="2" color="gray">The Worker reads canonical JSON event envelopes from this Source. There is no inbound HTTP endpoint for this type.</Text>
      {source.type === 'HTTP_POLLING' && !source.configuration.method && <Text size="2"><strong>method:</strong> GET</Text>}
      {sourceFields[source.type].map((field) => source.configuration[field] != null &&
        <Text key={field} size="2"><strong>{field}:</strong> {typeof source.configuration[field] === 'object'
          ? JSON.stringify(source.configuration[field]) : String(source.configuration[field])}</Text>)}
      </Flex></Card>}
    </div>
    {source.type === 'HTTP_API' && <div id="source-panel-events" role="tabpanel" aria-labelledby="source-tab-events" className="source-detail-content" hidden={activeTab !== 'events'}><HttpSourceDetails source={source} /></div>}
  </div>
}
