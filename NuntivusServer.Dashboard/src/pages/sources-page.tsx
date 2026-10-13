import { Badge, Button, Card, Flex, Heading, Table, Text } from '@radix-ui/themes'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { frontendConfig } from '../app/frontend-config'
import { CopyIcon } from '../components/icons/nav-icons'
import {
  useDeleteSourceMutation, useSourcesQuery, useToggleSourceMutation,
} from '../features/sources/use-sources'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { getApiErrorMessage } from '../services/api/api-client'
import type { Source } from '../services/api/sources-api'
import { useAppSelector } from '../store/hooks'

export function SourcesPage() {
  const { currentUser } = useAppSelector((state) => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const sources = useSourcesQuery()
  const toggle = useToggleSourceMutation()
  const remove = useDeleteSourceMutation()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  async function changeState(source: Source) {
    try {
      await toggle.mutateAsync({ id: source.id, enabled: !source.enabled })
      notifications.success(`Source ${source.enabled ? 'disabled' : 'enabled'}.`, 'Sources')
    } catch (cause) { notifications.error(getApiErrorMessage(cause, 'Unable to update Source.'), 'Sources') }
  }

  async function deleteItem(source: Source) {
    if (!await confirm({ title: 'Delete Source', description: `Delete ${source.name}? Sources referenced by contracts or events cannot be deleted.`, confirmText: 'Delete Source', confirmColor: 'red' })) return
    try {
      await remove.mutateAsync(source.id)
      notifications.success('Source deleted.', 'Sources')
    } catch (cause) { notifications.error(getApiErrorMessage(cause, 'Unable to delete Source.'), 'Sources') }
  }

  async function copyEndpoint(source: Source) {
    try {
      await navigator.clipboard.writeText(`${frontendConfig.apiBaseUrl}/v1/sources/${source.id}/events`)
      notifications.success('Ingestion endpoint copied.', 'Sources')
    } catch {
      notifications.error('Unable to copy ingestion endpoint.', 'Sources')
    }
  }

  return <Flex direction="column" gap="4">
    <Flex justify="between" align="center"><Heading size="6">Sources</Heading>
      {canWrite && <Button asChild><NavLink to="/sources/new">Add Source</NavLink></Button>}
    </Flex>
    <Text size="2" color="gray">HTTP API Sources receive the configured POST, PUT or PATCH request; polling, broker and file Sources are consumed by the Worker.</Text>
    {sources.isLoading && <Text>Loading Sources...</Text>}
    {sources.isError && <Text color="red">{getApiErrorMessage(sources.error, 'Unable to load Sources.')}</Text>}
    {sources.data && <Card><Table.Root><Table.Header><Table.Row>
      <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Type</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Ingestion</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
    </Table.Row></Table.Header><Table.Body>{sources.data.map((source) => <Table.Row key={source.id}>
      <Table.Cell><NavLink to={`/sources/${encodeURIComponent(source.id)}`}>{source.name}</NavLink><Text as="p" size="1" color="gray">{source.description}</Text></Table.Cell>
      <Table.Cell>{source.type}</Table.Cell><Table.Cell><Badge color={source.enabled ? 'green' : 'gray'}>{source.enabled ? 'Enabled' : 'Disabled'}</Badge></Table.Cell>
      <Table.Cell>{source.type === 'HTTP_API' ? <Flex align="center" gap="2" wrap="wrap"><Text size="1" style={{ overflowWrap: 'anywhere' }}>{frontendConfig.apiBaseUrl}/v1/sources/{source.id}/events</Text><Button size="1" variant="soft" aria-label={`Copy endpoint for ${source.name}`} onClick={() => void copyEndpoint(source)}><CopyIcon /> Copy</Button></Flex> : <Text size="2">Worker consumer</Text>}</Table.Cell>
      <Table.Cell><Flex gap="2" wrap="wrap"><Button asChild size="1" variant="soft"><NavLink to={`/sources/${encodeURIComponent(source.id)}`}>Details</NavLink></Button>{canWrite && <><Button asChild size="1" variant="soft"><NavLink to={`/sources/${encodeURIComponent(source.id)}?edit=1`}>Edit</NavLink></Button>
        <Button size="1" variant="soft" onClick={() => void changeState(source)} disabled={toggle.isPending}>{source.enabled ? 'Disable' : 'Enable'}</Button>
        <Button size="1" variant="soft" color="red" onClick={() => void deleteItem(source)} disabled={remove.isPending}>Delete</Button></>}</Flex></Table.Cell>
    </Table.Row>)}</Table.Body></Table.Root>{sources.data.length === 0 && <Text color="gray">No Sources yet.</Text>}</Card>}
  </Flex>
}
