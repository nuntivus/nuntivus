import { Button, Card, Heading, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { flushSync } from 'react-dom'
import { FormErrorText } from '../../components/forms/form-feedback'
import { IntegrationCreationLayout } from '../../components/forms/integration-creation-layout'
import { DestinationConfigurationForm } from '../contracts/destination-forms/destination-configuration-form'
import { DestinationTypeSelector } from '../contracts/destination-forms/destination-type-selector'
import { createDefaultDestinationFormState, mapDestinationFormToConfig, validateDestinationFormState } from '../contracts/destination-forms/types'
import { useCreateDestinationMutation } from './use-destinations'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { useNotifications } from '../../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../../services/api/api-client'
import { mapDestinationToApi } from '../../services/api/contracts-api'
import { testDestinationConnection, type DestinationResource } from '../../services/api/destinations-api'

interface DestinationCreateFormProps {
  onSaved: (destination: DestinationResource) => void
  onCancel: () => void
}

export function DestinationCreateForm({ onSaved, onCancel }: DestinationCreateFormProps) {
  const [initialForm] = useState(() => createDefaultDestinationFormState())
  const [form, setForm] = useState(initialForm)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [testPayload, setTestPayload] = useState('{}')
  const [error, setError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ form: string; message: string; failed: boolean } | null>(null)
  const [saved, setSaved] = useState(false)
  const create = useCreateDestinationMutation()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const dirty = !!(name || description) || JSON.stringify(form) !== JSON.stringify(initialForm)
  useUnsavedChangesGuard({ when: dirty && !saved })

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (create.isPending || saved) return
    const validation = !name.trim() ? 'Name is required.' : validateDestinationFormState(form)
    if (validation) { setError(validation); return }
    setError(null)
    try {
      const result = await create.mutateAsync({
        name: name.trim(), description: description.trim() || null,
        configuration: mapDestinationFormToConfig(form),
      })
      flushSync(() => setSaved(true))
      notifications.success('Destination created.', 'Destinations')
      onSaved(result)
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to create Destination.')) }
  }

  async function testConnection() {
    const snapshot = JSON.stringify([form, testPayload])
    const validation = validateDestinationFormState(form)
    if (validation) { setError(validation); return }
    let payload: unknown
    if (form.type !== 'Http' || form.http.method !== 'GET') {
      try { payload = JSON.parse(testPayload) } catch { setError('Test payload must be valid JSON.'); return }
      if (new TextEncoder().encode(JSON.stringify(payload)).length > 65536) { setError('Test payload must not exceed 64 KiB.'); return }
    }
    if (!await confirm({ title: 'Send test request?', description: form.type === 'Http'
      ? `Sends a real ${form.http.method} request using the current URL, headers and authentication. This may trigger actions at the destination. No event or delivery is stored.`
      : `Publishes a real ${form.type} message using the current destination configuration and credentials. This may trigger downstream actions. No event or delivery is stored.`, confirmText: 'Send test request' })) return
    setError(null)
    setTesting(true)
    setTestResult(null)
    try {
      const result = await testDestinationConnection(mapDestinationToApi(mapDestinationFormToConfig(form)), undefined, payload)
      setTestResult({ form: snapshot, message: result.message, failed: false })
    } catch (cause) {
      setTestResult({ form: snapshot, message: getApiErrorMessage(cause, 'Connection test failed.'), failed: true })
    } finally { setTesting(false) }
  }

  return <Card className="settings-editor-card integration-creation-card configuration-editor"><form onSubmit={event => void submit(event)} className="settings-editor-form">
    <Heading size="5">Register a new destination</Heading>
    <Text size="2" color="gray">Configure where this Destination delivers events. Use managed secrets rather than entering sensitive values in plain text.</Text>
    <IntegrationCreationLayout picker={<DestinationTypeSelector value={form.type} onChange={type => setForm(createDefaultDestinationFormState(type))} />}>
      <div className="settings-editor-divider">Destination details</div>
      <div className="settings-editor-fields">
        <label className="settings-editor-field"><span>Name <span aria-hidden="true">*</span></span>
          <TextField.Root value={name} maxLength={200} required onChange={event => setName(event.target.value)} />
          <Text size="1" color="gray">A recognizable name for this Destination.</Text>
        </label>
        <label className="settings-editor-field source-description-field">Description
          <TextArea value={description} maxLength={1000} onChange={event => setDescription(event.target.value)} />
          <Text size="1" color="gray">Optional context for operators.</Text>
        </label>
      </div>
      <div className="settings-editor-divider">Connection settings</div>
      <DestinationConfigurationForm fixedType value={form} onChange={setForm} />
      <div className="settings-editor-field">
        <Text as="label" htmlFor="destination-test-payload">JSON test payload</Text>
        <TextArea id="destination-test-payload" value={testPayload} onChange={event => setTestPayload(event.target.value)}
          disabled={(form.type === 'Http' && form.http.method === 'GET') || testing} rows={6} />
        <Text size="1" color="gray">{form.type === 'Http' && form.http.method === 'GET' ? 'GET sends no request body.' : 'Test-only JSON payload (maximum 64 KiB). Not saved as destination configuration.'} Testing sends a real request or message and may trigger actions at the destination.</Text>
      </div>
      <FormErrorText message={error} />
      {testResult?.form === JSON.stringify([form, testPayload]) && <Text color={testResult.failed ? 'red' : 'green'} role="status">{testResult.message}</Text>}
      <div className="settings-editor-actions">
        <Button type="submit" disabled={create.isPending || testing}>Create destination</Button>
        <Button type="button" variant="soft" onClick={() => void testConnection()} disabled={testing || create.isPending}>{testing ? 'Testing...' : 'Test connection'}</Button>
        <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
      </div>
    </IntegrationCreationLayout>
  </form></Card>
}
