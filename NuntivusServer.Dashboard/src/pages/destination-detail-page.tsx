import { Badge, Button, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { NavLink, useParams } from 'react-router-dom'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { EventsIcon } from '../components/icons/nav-icons'
import { useDestinationDetailQuery } from '../features/destinations/use-destinations'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'

export function DestinationDetailPage() {
  const { id } = useParams()
  const query = useDestinationDetailQuery(id)
  if (!id) return <Card><Text color="red">Invalid Destination ID.</Text></Card>
  if (query.isLoading) return <DetailLoadingState message="Loading Destination..." />
  if (query.isError || !query.data) return <Card><Flex direction="column" gap="3">
    <Text color={query.error instanceof ApiError && query.error.status === 404 ? 'gray' : 'red'}>{getApiErrorMessage(query.error, 'Destination not found.')}</Text>
    <Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>
  </Flex></Card>
  const item = query.data
  return <Flex direction="column" gap="4" className="detail-page">
    <DetailPageHeader title={item.name} description="Shared destination configuration and contract usage." icon={EventsIcon}
      actions={<Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>} />
    <Card><Flex direction="column" gap="2"><Heading size="4">Overview</Heading>
      <Text size="2"><strong>Destination ID:</strong> {item.id}</Text>
      <Text size="2"><strong>Type:</strong> {item.type}</Text>
      <Text size="2"><strong>Status:</strong> <Badge color={item.enabled ? 'green' : 'gray'}>{item.enabled ? 'Enabled' : 'Disabled'}</Badge></Text>
      <Text size="2"><strong>Description:</strong> {item.description || '—'}</Text>
      <Text size="2"><strong>Created:</strong> {new Date(item.createdAt).toLocaleString()}</Text>
      <Text size="2"><strong>Updated:</strong> {new Date(item.updatedAt).toLocaleString()}</Text>
      <Text size="2">Edits here apply to new links or explicitly refreshed contract snapshots.</Text>
    </Flex></Card>
    <Card><Flex direction="column" gap="2"><Heading size="4">{item.type} configuration</Heading>
      <Text size="2" color="gray">Credentials are redacted. Refer to Secrets for managed values.</Text>
      <pre style={{ overflowX: 'auto', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{JSON.stringify(JSON.parse(item.configuration), null, 2)}</pre>
    </Flex></Card>
  </Flex>
}
