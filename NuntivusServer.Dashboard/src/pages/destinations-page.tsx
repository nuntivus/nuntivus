import { Badge, Button, Card, Flex, Heading, Table, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { flushSync } from 'react-dom'
import { NavLink, useNavigate } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FormDialog } from '../components/dialogs/form-dialog'
import { DetailIndicator, NavigableTableRow } from '../components/tables/table-actions'
import { DestinationTypeSelector } from '../features/contracts/destination-forms/destination-type-selector'
import { normalizeDestinationType } from '../features/contracts/destination-forms/types'
import { useCreateDestinationDraftMutation, useDeleteDestinationMutation, useDestinationsQuery, useToggleDestinationMutation } from '../features/destinations/use-destinations'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../services/api/api-client'
import type { DestinationType } from '../services/api/contracts-api'
import type { DestinationResource } from '../services/api/destinations-api'
import { useAppSelector } from '../store/hooks'

export function DestinationsPage() {
  const navigate = useNavigate()
  const { currentUser } = useAppSelector((state) => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const list = useDestinationsQuery()
  const create = useCreateDestinationDraftMutation()
  const toggle = useToggleDestinationMutation()
  const remove = useDeleteDestinationMutation()
  const notifications = useNotifications()
  const [formOpen, setFormOpen] = useState(false)
  const [choice, setChoice] = useState<{ kind: 'disable' | 'delete'; item: DestinationResource } | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [type, setType] = useState<DestinationType>('Http')
  const [error, setError] = useState<string | null>(null)
  useUnsavedChangesGuard({ when: formOpen && !!(name || description || type !== 'Http') })

  function open() {
    setName('')
    setDescription('')
    setType('Http')
    setError(null)
    setFormOpen(true)
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!name.trim()) { setError('Name is required.'); return }
    try {
      const result = await create.mutateAsync({ name: name.trim(), description: description.trim() || null, type })
      flushSync(() => setFormOpen(false))
      notifications.success('Destination created. Configure it before enabling.', 'Destinations')
      navigate(`/destinations/${encodeURIComponent(result.id)}?edit=1`)
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to save Destination.')) }
  }

  async function choose(pauseOrRemove: boolean) {
    if (!choice) return
    try {
      if (choice.kind === 'delete') await remove.mutateAsync({ id: choice.item.id, removeBindings: pauseOrRemove })
      else await toggle.mutateAsync({ id: choice.item.id, enabled: false, pauseExistingBindings: pauseOrRemove })
      notifications.success(`Destination ${choice.kind === 'delete' ? 'deleted' : 'disabled'}.`, 'Destinations')
      setChoice(null)
    } catch (cause) { notifications.error(getApiErrorMessage(cause, 'Unable to change Destination.'), 'Destinations') }
  }

  return <Flex direction="column" gap="4">
    <Flex justify="between" align="center"><Heading size="6">Destinations</Heading>
      {canWrite && <Button onClick={open}>Add Destination</Button>}
    </Flex>
    <Text size="2" color="gray">Destinations are global. Contracts copy their configuration when linked; changes here do not update existing contract snapshots.</Text>
    {list.isLoading && <Text>Loading Destinations...</Text>}
    {list.isError && <Text color="red">{getApiErrorMessage(list.error, 'Unable to load Destinations.')}</Text>}
    {list.data && <Card><Table.Root><Table.Header><Table.Row>
      <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Type</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
    </Table.Row></Table.Header><Table.Body>{list.data.map(item => <NavigableTableRow key={item.id} to={`/destinations/${encodeURIComponent(item.id)}`} label={`Destination ${item.name}`}>
      <Table.Cell>{item.name}<Text as="p" size="1" color="gray">{item.description}</Text></Table.Cell>
      <Table.Cell>{normalizeDestinationType(item.type) ?? String(item.type)}</Table.Cell><Table.Cell><Badge color={item.configured ? item.enabled ? 'green' : 'gray' : 'amber'}>{item.configured ? item.enabled ? 'Enabled' : 'Disabled' : 'Needs configuration'}</Badge></Table.Cell>
      <Table.Cell><Flex gap="2" align="center" wrap="wrap">{canWrite && <>
        <Button asChild size="1" variant="soft"><NavLink to={`/destinations/${encodeURIComponent(item.id)}?edit=1`}>Edit</NavLink></Button>
        {item.enabled ? <Button size="1" variant="soft" onClick={() => setChoice({ kind: 'disable', item })}>Disable</Button>
          : item.configured && <Button size="1" variant="soft" onClick={() => void toggle.mutateAsync({ id: item.id, enabled: true, pauseExistingBindings: false })}>Enable</Button>}
        <Button size="1" color="red" variant="soft" onClick={() => setChoice({ kind: 'delete', item })}>Delete</Button>
      </>}<DetailIndicator /></Flex></Table.Cell>
    </NavigableTableRow>)}</Table.Body></Table.Root></Card>}
    <FormDialog open={formOpen} onOpenChange={setFormOpen} title="Add Destination"
      footer={<><Button variant="soft" type="button" onClick={() => setFormOpen(false)}>Cancel</Button><Button type="submit" form="destination-form" disabled={create.isPending}>Save</Button></>}>
      <form id="destination-form" onSubmit={(event) => void submit(event)}><Flex direction="column" gap="3">
        <TextField.Root placeholder="Name" aria-label="Destination name" value={name} onChange={event => setName(event.target.value)} required />
        <TextField.Root placeholder="Description" aria-label="Description" value={description} onChange={event => setDescription(event.target.value)} />
        <DestinationTypeSelector value={type} onChange={setType} />
        {error && <Text color="red">{error}</Text>}
      </Flex></form>
    </FormDialog>
    <FormDialog open={!!choice} onOpenChange={open => { if (!open) setChoice(null) }}
      title={choice?.kind === 'disable' ? 'Disable Destination' : 'Delete Destination'}>
      <Flex direction="column" gap="3"><Text>Choose what happens to contracts already linked to {choice?.item.name}. Existing deliveries retain their snapshots.</Text>
        <Button type="button" variant="soft" onClick={() => void choose(false)}>{choice?.kind === 'disable' ? 'Continue existing contract deliveries' : 'Keep detached contract snapshots'}</Button>
        <Button type="button" color="red" variant="soft" onClick={() => void choose(true)}>{choice?.kind === 'disable' ? 'Pause new deliveries on existing links' : 'Remove contract links and stop new deliveries'}</Button>
        <Button type="button" variant="ghost" onClick={() => setChoice(null)}>Cancel</Button>
      </Flex>
    </FormDialog>
  </Flex>
}
