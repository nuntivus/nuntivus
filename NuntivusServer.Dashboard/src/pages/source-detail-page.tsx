import { Badge, Button, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { NavLink, useParams } from 'react-router-dom'
import { frontendConfig } from '../app/frontend-config'
import { CopyIcon, EventsIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { useSourceDetailQuery } from '../features/sources/use-sources'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { Source } from '../services/api/sources-api'
import { sourceFields } from '../features/sources/source-types'

function HttpSourceDetails({ source }: { source: Source }) {
  const notifications = useNotifications()
  const endpoint = `${frontendConfig.apiBaseUrl}/v1/sources/${source.id}/events`
  const tokenRequired = source.configuration.authentication?.type === 'token'
  const example = [
    `curl -X POST '${endpoint}' \\`,
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
      <Text size="2" color="gray">Send POST requests to this Source’s unique endpoint. The Source must be enabled to accept events.</Text>
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
  const query = useSourceDetailQuery(id)

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
  return <Flex direction="column" gap="4" className="detail-page">
    <DetailPageHeader title={source.name} description="Source configuration and event ingestion details." icon={EventsIcon}
      actions={<Button asChild variant="soft"><NavLink to="/sources">Back to Sources</NavLink></Button>} />
    <Card><Flex direction="column" gap="2">
      <Heading size="4">Overview</Heading>
      <Text size="2"><strong>Source ID:</strong> {source.id}</Text>
      <Text size="2"><strong>Type:</strong> {source.type}</Text>
      <Text size="2"><strong>Status:</strong> <Badge color={source.enabled ? 'green' : 'gray'}>{source.enabled ? 'Enabled' : 'Disabled'}</Badge></Text>
      <Text size="2"><strong>Description:</strong> {source.description || '—'}</Text>
      <Text size="2"><strong>Created:</strong> {new Date(source.createdAt).toLocaleString()}</Text>
      <Text size="2"><strong>Last updated:</strong> {new Date(source.updatedAt).toLocaleString()}</Text>
    </Flex></Card>
    {source.type === 'HTTP_API' && <HttpSourceDetails source={source} />}
    {sourceFields[source.type] && <Card><Flex direction="column" gap="2">
      <Heading size="4">{source.type} consumer</Heading>
      <Text size="2" color="gray">The Worker reads canonical JSON event envelopes from this Source. There is no inbound HTTP endpoint for this type.</Text>
      {sourceFields[source.type].map((field) => source.configuration[field] != null &&
        <Text key={field} size="2"><strong>{field}:</strong> {field === 'secretRef' ? String(source.configuration[field]) : String(source.configuration[field])}</Text>)}
    </Flex></Card>}
  </Flex>
}
