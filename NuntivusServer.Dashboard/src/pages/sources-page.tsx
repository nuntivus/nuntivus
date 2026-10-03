import { Badge, Button, Card, Flex, Heading, Select, Table, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { frontendConfig } from '../app/frontend-config'
import { FormDialog } from '../components/dialogs/form-dialog'
import { CopyIcon } from '../components/icons/nav-icons'
import { useSecretsQuery } from '../features/contracts/use-secrets'
import { sourceFields, sourceTypes } from '../features/sources/source-types'
import {
  useCreateSourceMutation, useDeleteSourceMutation, useSourcesQuery,
  useToggleSourceMutation, useUpdateSourceMutation,
} from '../features/sources/use-sources'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../services/api/api-client'
import type { Source, SourceInput } from '../services/api/sources-api'
import { useAppSelector } from '../store/hooks'

type Draft = { name: string; description: string; type: string; authType: 'none' | 'token'; secretRef: string; fields: Record<string, string> }
const emptyDraft: Draft = { name: '', description: '', type: 'HTTP_API', authType: 'none', secretRef: '', fields: {} }

export function SourcesPage() {
  const { currentUser } = useAppSelector((state) => state.auth)
  const canWrite = hasPermission(currentUser, 'contracts.write')
  const sources = useSourcesQuery()
  const secrets = useSecretsQuery()
  const create = useCreateSourceMutation()
  const update = useUpdateSourceMutation()
  const toggle = useToggleSourceMutation()
  const remove = useDeleteSourceMutation()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<Source | null>(null)
  const [draft, setDraft] = useState<Draft>(emptyDraft)
  const [error, setError] = useState<string | null>(null)
  useUnsavedChangesGuard({ when: open && !!(draft.name || draft.description || draft.secretRef || Object.values(draft.fields).some(Boolean)) })

  function close() {
    setOpen(false)
    setEditing(null)
    setDraft(emptyDraft)
    setError(null)
  }

  function edit(source: Source) {
    setEditing(source)
    setDraft({ name: source.name, description: source.description ?? '', type: source.type,
      authType: source.configuration.authentication?.type ?? 'none',
      secretRef: source.configuration.authentication?.secretRef ?? '',
      fields: Object.fromEntries(Object.entries(source.configuration).map(([key, value]) => [key, String(value)])) })
    setOpen(true)
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const requiredFields = sourceFields[draft.type]?.filter((key) => !['secretRef', 'username', 'accessKeyId', 'intervalSeconds'].includes(key)) ?? []
    if (!draft.name.trim() || (draft.type === 'HTTP_API' && draft.authType === 'token' && !draft.secretRef)
      || requiredFields.some((key) => !draft.fields[key]?.trim())) return
    const input: SourceInput = {
      type: draft.type, name: draft.name.trim(), description: draft.description.trim() || null,
      configuration: draft.type === 'HTTP_API'
        ? { authentication: draft.authType === 'none' ? { type: 'none' } : { type: 'token', secretRef: draft.secretRef } }
        : Object.fromEntries(sourceFields[draft.type].filter((key) => draft.fields[key]?.trim())
          .map((key) => [key, key === 'intervalSeconds' ? Number(draft.fields[key]) : draft.fields[key].trim()])),
    }
    try {
      if (editing) await update.mutateAsync({ id: editing.id, input })
      else await create.mutateAsync(input)
      close()
      notifications.success('Source saved.', 'Sources')
    } catch (cause) {
      setError(getApiErrorMessage(cause, 'Unable to save Source.'))
    }
  }

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
      {canWrite && <Button onClick={() => { setDraft(emptyDraft); setEditing(null); setOpen(true) }}>Add Source</Button>}
    </Flex>
    <Text size="2" color="gray">HTTP Sources receive POST requests; broker, polling and file Sources are consumed by the Worker.</Text>
    {sources.isLoading && <Text>Loading Sources...</Text>}
    {sources.isError && <Text color="red">{getApiErrorMessage(sources.error, 'Unable to load Sources.')}</Text>}
    {sources.data && <Card><Table.Root><Table.Header><Table.Row>
      <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Type</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Ingestion</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Actions</Table.ColumnHeaderCell>
    </Table.Row></Table.Header><Table.Body>{sources.data.map((source) => <Table.Row key={source.id}>
      <Table.Cell><NavLink to={`/sources/${encodeURIComponent(source.id)}`}>{source.name}</NavLink><Text as="p" size="1" color="gray">{source.description}</Text></Table.Cell>
      <Table.Cell>{source.type}</Table.Cell><Table.Cell><Badge color={source.enabled ? 'green' : 'gray'}>{source.enabled ? 'Enabled' : 'Disabled'}</Badge></Table.Cell>
      <Table.Cell>{source.type === 'HTTP_API' ? <Flex align="center" gap="2" wrap="wrap"><Text size="1" style={{ overflowWrap: 'anywhere' }}>{frontendConfig.apiBaseUrl}/v1/sources/{source.id}/events</Text><Button size="1" variant="soft" aria-label={`Copy endpoint for ${source.name}`} onClick={() => void copyEndpoint(source)}><CopyIcon /> Copy</Button></Flex> : <Text size="2">Worker consumer</Text>}</Table.Cell>
      <Table.Cell><Flex gap="2" wrap="wrap"><Button asChild size="1" variant="soft"><NavLink to={`/sources/${encodeURIComponent(source.id)}`}>Details</NavLink></Button>{canWrite && <><Button size="1" variant="soft" onClick={() => edit(source)}>Edit</Button>
        <Button size="1" variant="soft" onClick={() => void changeState(source)} disabled={toggle.isPending}>{source.enabled ? 'Disable' : 'Enable'}</Button>
        <Button size="1" variant="soft" color="red" onClick={() => void deleteItem(source)} disabled={remove.isPending}>Delete</Button></>}</Flex></Table.Cell>
    </Table.Row>)}</Table.Body></Table.Root>{sources.data.length === 0 && <Text color="gray">No Sources yet.</Text>}</Card>}
    <FormDialog open={open} onOpenChange={(value) => { if (!value) close() }} title={editing ? 'Edit Source' : 'Add Source'}
      footer={<><Button variant="soft" color="gray" onClick={close}>Cancel</Button><Button type="submit" form="source-form" disabled={create.isPending || update.isPending || !draft.name.trim()}>Save Source</Button></>}>
      <form id="source-form" onSubmit={(event) => void submit(event)}><Flex direction="column" gap="3">
        <label>Name<TextField.Root value={draft.name} maxLength={200} onChange={(event) => setDraft({ ...draft, name: event.target.value })} /></label>
        <label>Description<TextArea value={draft.description} maxLength={1000} onChange={(event) => setDraft({ ...draft, description: event.target.value })} /></label>
        <Text size="2">Type</Text><Select.Root value={draft.type} onValueChange={(type) => setDraft({ ...draft, type, fields: {} })} disabled={!!editing}>
          <Select.Trigger aria-label="Source type" /><Select.Content>{sourceTypes.map((type) => <Select.Item key={type} value={type}>{type}</Select.Item>)}</Select.Content>
        </Select.Root>
        {draft.type === 'HTTP_API' && <><Text size="2">Authentication</Text><Select.Root value={draft.authType} onValueChange={(value: 'none' | 'token') => setDraft({ ...draft, authType: value })}>
          <Select.Trigger aria-label="Source authentication" /><Select.Content><Select.Item value="none">None</Select.Item><Select.Item value="token">Token</Select.Item></Select.Content>
        </Select.Root>
        {draft.authType === 'token' && <><Text size="2">Managed token secret</Text><Select.Root value={draft.secretRef} onValueChange={(value) => setDraft({ ...draft, secretRef: value })}>
          <Select.Trigger placeholder="Select a managed secret" aria-label="Managed token secret" /><Select.Content>{secrets.data?.map((secret) => <Select.Item value={secret.name} key={secret.name}>{secret.name}</Select.Item>)}</Select.Content>
        </Select.Root><Text size="1" color="gray">Create the token in Secrets first. The secret value is never shown here.</Text></>}</>}
        {sourceFields[draft.type]?.map((field) => <label key={field}>{field === 'secretRef' ? 'Managed secret (optional)' : field}
          {field === 'secretRef' ? <Select.Root value={draft.fields[field] ?? ''} onValueChange={(value) => setDraft({ ...draft, fields: { ...draft.fields, [field]: value } })}>
            <Select.Trigger placeholder="Select a managed secret" /><Select.Content>{secrets.data?.map((secret) => <Select.Item value={secret.name} key={secret.name}>{secret.name}</Select.Item>)}</Select.Content>
          </Select.Root> : <TextField.Root value={draft.fields[field] ?? ''} onChange={(event) => setDraft({ ...draft, fields: { ...draft.fields, [field]: event.target.value } })} />}
        </label>)}
        {error && <Text color="red" role="alert">{error}</Text>}
      </Flex></form>
    </FormDialog>
  </Flex>
}
