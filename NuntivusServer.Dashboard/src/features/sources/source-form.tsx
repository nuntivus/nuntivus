import { Button, Card, Heading, Select, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { flushSync } from 'react-dom'
import { useSecretsQuery } from '../contracts/use-secrets'
import { useCreateSourceMutation, useUpdateSourceMutation } from './use-sources'
import { sourceConfiguration, sourceConfigurationFields, sourceFields, sourceTypes, validatePollingJson } from './source-types'
import { useNotifications } from '../../hooks/use-notifications'
import { EventsIcon } from '../../components/icons/nav-icons'
import { useUnsavedChangesGuard } from '../../hooks/use-unsaved-changes-guard'
import { ApiError, getApiErrorMessage } from '../../services/api/api-client'
import { testSourceConnection, type Source, type SourceInput } from '../../services/api/sources-api'

type SourceDraft = {
  name: string
  description: string
  type: string
  authType: 'none' | 'token'
  secretRef: string
  method: string
  fields: Record<string, string>
}

type SourceFormProps = {
  source?: Source
  onSaved: (source: Source) => void
  onCancel: () => void
}

function initialSourceDraft(source?: Source): SourceDraft {
  return source ? {
    name: source.name,
    description: source.description ?? '',
    type: source.type,
    authType: source.configuration.authentication?.type ?? 'none',
    secretRef: source.configuration.authentication?.secretRef ?? '',
    method: typeof source.configuration.method === 'string' ? source.configuration.method : 'POST',
    fields: sourceConfigurationFields(source.configuration),
  } : { name: '', description: '', type: 'HTTP_API', authType: 'none', secretRef: '', method: 'POST', fields: {} }
}

function sourceFieldLabel(field: string) {
  const labels: Record<string, string> = {
    url: 'URL', uri: 'URI', queueUrl: 'Queue URL', secretRef: 'Managed secret (optional)',
    accessKeyId: 'Access key ID', intervalSeconds: 'Polling interval (seconds)',
    headers: 'Plain headers (JSON)', body: 'Request body (JSON)', method: 'Polling method',
  }
  return labels[field] ?? field.replace(/([A-Z])/g, ' $1').replace(/^./, letter => letter.toUpperCase())
}

export function SourceForm({ source, onSaved, onCancel }: SourceFormProps) {
  const [initialDraft] = useState<SourceDraft>(() => initialSourceDraft(source))
  const [draft, setDraft] = useState<SourceDraft>(initialDraft)
  const [error, setError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ draft: string; message: string; failed: boolean } | null>(null)
  const [saved, setSaved] = useState(false)
  const create = useCreateSourceMutation()
  const update = useUpdateSourceMutation()
  const secrets = useSecretsQuery()
  const notifications = useNotifications()
  const pending = create.isPending || update.isPending
  useUnsavedChangesGuard({ when: !saved && JSON.stringify(draft) !== JSON.stringify(initialDraft) })

  function sourceInput(): SourceInput {
    return {
      type: draft.type,
      name: draft.name.trim(),
      description: draft.description.trim() || null,
      configuration: sourceConfiguration(draft.type, draft.authType, draft.secretRef, draft.method, draft.fields),
    }
  }

  async function testConnection() {
    const snapshot = JSON.stringify(draft)
    setTesting(true)
    setTestResult(null)
    try {
      if (draft.type === 'HTTP_POLLING') validatePollingJson(draft.fields)
      const result = await testSourceConnection(sourceInput(), source?.id)
      setTestResult({ draft: snapshot, message: result.message, failed: false })
    } catch (cause) {
      setTestResult({ draft: snapshot, message: cause instanceof Error && !(cause instanceof ApiError)
        ? cause.message : getApiErrorMessage(cause, 'Connection test failed.'), failed: true })
    } finally { setTesting(false) }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    const requiredFields = sourceFields[draft.type]?.filter(field =>
      !['secretRef', 'username', 'accessKeyId', 'intervalSeconds', 'method', 'headers', 'body'].includes(field)) ?? []
    if (!draft.name.trim() || (draft.type === 'HTTP_API' && draft.authType === 'token' && !draft.secretRef)
      || requiredFields.some(field => !draft.fields[field]?.trim())) {
      setError('Complete the name, required configuration fields, and selected authentication before saving.')
      return
    }
    setError(null)
    try {
      if (draft.type === 'HTTP_POLLING') validatePollingJson(draft.fields)
      const input = sourceInput()
      const result = source ? await update.mutateAsync({ id: source.id, input }) : await create.mutateAsync(input)
      flushSync(() => setSaved(true))
      notifications.success('Source saved.', 'Sources')
      onSaved(result)
    } catch (cause) {
      setError(cause instanceof Error && !(cause instanceof ApiError)
        ? cause.message : getApiErrorMessage(cause, 'Unable to save Source.'))
    }
  }

  return <Card id={source ? 'source-configuration' : undefined} className={`settings-editor-card${source ? ' source-configuration-card' : ''}`}><form onSubmit={(event) => void submit(event)} className="settings-editor-form">
    {source ? <div className="source-configuration-heading"><span className="source-configuration-icon"><EventsIcon /></span><div><Heading size="4">{source.type === 'HTTP_API' ? 'HTTP Source Configuration' : `${source.type} Source Configuration`}</Heading><Text size="2" color="gray">Configure how this Source receives events. Use managed secrets rather than entering sensitive values in plain text.</Text></div></div>
      : <Heading size="5">Register a new source</Heading>}
    {!source && <>
    <Text size="2" color="gray">Configure how this Source receives events. Use managed secrets rather than entering sensitive values in plain text.</Text>
    <div className="settings-editor-divider">Source details</div>
    </>}
    <div className="settings-editor-fields">
      <label className="settings-editor-field"><span>Name <span aria-hidden="true">*</span></span>
        <TextField.Root value={draft.name} maxLength={200} required onChange={event => setDraft({ ...draft, name: event.target.value })} />
        <Text size="1" color="gray">A recognizable name for this Source.</Text>
      </label>
      <label className="settings-editor-field">Type
        <Select.Root value={draft.type} onValueChange={type => setDraft({ ...draft, type, fields: {} })} disabled={!!source}>
          <Select.Trigger aria-label="Source type" /><Select.Content>{sourceTypes.map(type => <Select.Item key={type} value={type}>{type}</Select.Item>)}</Select.Content>
        </Select.Root>
      </label>
      <label className="settings-editor-field source-description-field">Description
        <TextArea value={draft.description} maxLength={1000} onChange={event => setDraft({ ...draft, description: event.target.value })} />
        <Text size="1" color="gray">Optional context for operators.</Text>
      </label>
    </div>
    {source ? <details className="source-advanced-settings"><summary>Advanced Settings</summary><div className="settings-editor-fields">
      {draft.type === 'HTTP_API' && <Text size="2" color="gray">Authentication and ingestion settings</Text>}
      {draft.type !== 'HTTP_API' && <Text size="2" color="gray">Connection settings</Text>}
      {configurationFields()}
    </div></details> : <>
    <div className="settings-editor-divider">Connection settings</div>
    <div className="settings-editor-fields">
      {configurationFields()}
    </div>
    </>}
    {error && <Text color="red" role="alert">{error}</Text>}
    <Text size="1" color="gray">Tests use the current configuration and credentials without consuming source data. Broker tests inspect metadata only; HTTP polling uses HEAD, not the configured retrieval method. Inbound HTTP tests validate local configuration only. No messages are acknowledged, offsets advanced, or files/objects moved or deleted.</Text>
    {testResult?.draft === JSON.stringify(draft) && <Text color={testResult.failed ? 'red' : 'green'} role="status">{testResult.message}</Text>}
    <div className="settings-editor-actions">
      <Button type="submit" disabled={pending}>{source ? 'Save changes' : 'Create source'}</Button>
      <Button type="button" variant="soft" onClick={() => void testConnection()} disabled={testing || pending}>{testing ? 'Testing...' : 'Test connection'}</Button>
      <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
    </div>
  </form></Card>

  function configurationFields() {
    return <>
      {draft.type === 'HTTP_API' && <>
        <label className="settings-editor-field">Authentication
          <Select.Root value={draft.authType} onValueChange={(authType: 'none' | 'token') => setDraft({ ...draft, authType })}>
            <Select.Trigger aria-label="Source authentication" /><Select.Content><Select.Item value="none">None</Select.Item><Select.Item value="token">Token</Select.Item></Select.Content>
          </Select.Root>
        </label>
        <label className="settings-editor-field">Ingestion method
          <Select.Root value={draft.method} onValueChange={method => setDraft({ ...draft, method })}>
            <Select.Trigger aria-label="Ingestion method" /><Select.Content>{['POST', 'PUT', 'PATCH'].map(method => <Select.Item key={method} value={method}>{method}</Select.Item>)}</Select.Content>
          </Select.Root>
          <Text size="1" color="gray">Only this method will be accepted at the Source endpoint.</Text>
        </label>
        {draft.authType === 'token' && <label className="settings-editor-field">Managed token secret
          <Select.Root value={draft.secretRef} onValueChange={secretRef => setDraft({ ...draft, secretRef })}>
            <Select.Trigger placeholder="Select a managed secret" aria-label="Managed token secret" />
            <Select.Content>{secrets.data?.map(secret => <Select.Item value={secret.name} key={secret.name}>{secret.name}</Select.Item>)}</Select.Content>
          </Select.Root>
          <Text size="1" color="gray">Create the token in Secrets first. Its value is never shown here.</Text>
        </label>}
      </>}
      {sourceFields[draft.type]?.map(field => <label key={field} className="settings-editor-field">
        {sourceFieldLabel(field)}
        {field === 'secretRef' ? <Select.Root value={draft.fields[field] ?? ''} onValueChange={value => setDraft({ ...draft, fields: { ...draft.fields, [field]: value } })}>
          <Select.Trigger placeholder="Select a managed secret" /><Select.Content>{secrets.data?.map(secret => <Select.Item value={secret.name} key={secret.name}>{secret.name}</Select.Item>)}</Select.Content>
        </Select.Root> : field === 'method' ? <Select.Root value={draft.fields.method || 'GET'} onValueChange={method => setDraft({ ...draft, fields: { ...draft.fields, method, ...(method === 'GET' ? { body: '' } : {}) } })}>
          <Select.Trigger aria-label="Polling method" /><Select.Content><Select.Item value="GET">GET</Select.Item><Select.Item value="POST">POST</Select.Item></Select.Content>
        </Select.Root> : field === 'body' || field === 'headers' ? <TextArea placeholder={field === 'headers' ? '{"X-Client": "nuntivus"}' : '{"page": 1}'} value={draft.fields[field] ?? ''} onChange={event => setDraft({ ...draft, fields: { ...draft.fields, [field]: event.target.value } })} />
          : <TextField.Root value={draft.fields[field] ?? ''} onChange={event => setDraft({ ...draft, fields: { ...draft.fields, [field]: event.target.value } })} />}
        {field === 'url' && <Text size="1" color="gray">Use the secure endpoint the Worker should poll.</Text>}
        {field === 'headers' && <Text size="1" color="gray">Plain string headers only; do not put secrets here.</Text>}
        {field === 'body' && <Text size="1" color="gray">Optional JSON body for POST polling requests.</Text>}
      </label>)}
    </>
  }
}
