import { Badge, Button, Card, Flex, Heading, Table, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FormDialog } from '../components/dialogs/form-dialog'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import { createDefaultDestinationFormState, mapDestinationFormToConfig, validateDestinationFormState } from '../features/contracts/destination-forms/types'
import { normalizeConfiguration } from '../features/contracts/destination-forms/normalize-configuration'
import { useCreateDestinationMutation, useDeleteDestinationMutation, useDestinationsQuery, useToggleDestinationMutation, useUpdateDestinationMutation } from '../features/destinations/use-destinations'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../services/api/api-client'
import type { DestinationConfigInput } from '../services/api/contracts-api'
import type { DestinationResource } from '../services/api/destinations-api'
import { useAppSelector } from '../store/hooks'

export function DestinationsPage() {
  const { currentUser } = useAppSelector((state) => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const list = useDestinationsQuery()
  const create = useCreateDestinationMutation()
  const update = useUpdateDestinationMutation()
  const toggle = useToggleDestinationMutation()
  const remove = useDeleteDestinationMutation()
  const notifications = useNotifications()
  const [editing, setEditing] = useState<DestinationResource | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [choice, setChoice] = useState<{ kind: 'disable' | 'delete'; item: DestinationResource } | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [form, setForm] = useState(createDefaultDestinationFormState())
  const [json, setJson] = useState('')
  const [error, setError] = useState<string | null>(null)
  useUnsavedChangesGuard({ when: formOpen && !!(name || description || json) })

  function open(item: DestinationResource | null) {
    setEditing(item)
    setName(item?.name ?? '')
    setDescription(item?.description ?? '')
    setJson(item ? JSON.stringify({ ...normalizeConfiguration(JSON.parse(item.configuration)) as Record<string, unknown>,
      type: item.type.toLowerCase() }, null, 2) : '')
    setForm(createDefaultDestinationFormState())
    setError(null)
    setFormOpen(true)
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!name.trim()) { setError('Name is required.'); return }
    try {
      let configuration: DestinationConfigInput | Record<string, unknown>
      if (editing) {
        configuration = normalizeConfiguration(JSON.parse(json)) as Record<string, unknown>
        if (configuration.type !== editing.type.toLowerCase()) {
          setError('Changing the destination type is not supported.')
          return
        }
      } else {
        const validation = validateDestinationFormState(form)
        if (validation) { setError(validation); return }
        configuration = mapDestinationFormToConfig(form)
      }
      const input = { name: name.trim(), description: description.trim() || null, configuration,
        rawConfiguration: !!editing }
      if (editing) await update.mutateAsync({ id: editing.id, input })
      else await create.mutateAsync(input)
      setFormOpen(false)
      notifications.success('Destination saved.', 'Destinations')
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
      {canWrite && <Button onClick={() => open(null)}>Add Destination</Button>}
    </Flex>
    <Text size="2" color="gray">Destinations are global. Contracts copy their configuration when linked; changes here do not update existing contract snapshots.</Text>
    {list.isLoading && <Text>Loading Destinations...</Text>}
    {list.isError && <Text color="red">{getApiErrorMessage(list.error, 'Unable to load Destinations.')}</Text>}
    {list.data && <Card><Table.Root><Table.Header><Table.Row>
      <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Type</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
    </Table.Row></Table.Header><Table.Body>{list.data.map(item => <Table.Row key={item.id}>
      <Table.Cell><NavLink to={`/destinations/${encodeURIComponent(item.id)}`}>{item.name}</NavLink><Text as="p" size="1" color="gray">{item.description}</Text></Table.Cell>
      <Table.Cell>{item.type}</Table.Cell><Table.Cell><Badge color={item.enabled ? 'green' : 'gray'}>{item.enabled ? 'Enabled' : 'Disabled'}</Badge></Table.Cell>
      <Table.Cell><Flex gap="2" wrap="wrap">{canWrite && <>
        <Button size="1" variant="soft" onClick={() => open(item)}>Edit</Button>
        {item.enabled ? <Button size="1" variant="soft" onClick={() => setChoice({ kind: 'disable', item })}>Disable</Button>
          : <Button size="1" variant="soft" onClick={() => void toggle.mutateAsync({ id: item.id, enabled: true, pauseExistingBindings: false })}>Enable</Button>}
        <Button size="1" color="red" variant="soft" onClick={() => setChoice({ kind: 'delete', item })}>Delete</Button>
      </>}</Flex></Table.Cell>
    </Table.Row>)}</Table.Body></Table.Root></Card>}
    <FormDialog open={formOpen} onOpenChange={setFormOpen} title={editing ? 'Edit Destination' : 'Add Destination'}
      footer={<><Button variant="soft" type="button" onClick={() => setFormOpen(false)}>Cancel</Button><Button type="submit" form="destination-form" disabled={create.isPending || update.isPending}>Save</Button></>}>
      <form id="destination-form" onSubmit={(event) => void submit(event)}><Flex direction="column" gap="3">
        <TextField.Root placeholder="Name" aria-label="Destination name" value={name} onChange={event => setName(event.target.value)} required />
        <TextField.Root placeholder="Description" aria-label="Description" value={description} onChange={event => setDescription(event.target.value)} />
        {editing ? <><Text size="2">{editing.type} configuration (secrets are redacted). Changes affect future contract links only.</Text>
          <TextArea aria-label="Destination configuration JSON" rows={10} value={json} onChange={event => setJson(event.target.value)} /></>
          : <DestinationConfigurationForm value={form} onChange={setForm} />}
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
