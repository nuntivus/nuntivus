import { Badge, Button, Flex, Select, Text, TextArea } from '@radix-ui/themes'
import { ConfigurationGrid, ConfigurationSection } from '../../components/forms/configuration-layout'
import { EventsIcon } from '../../components/icons/nav-icons'
import { useState } from 'react'
import { FormErrorText } from '../../components/forms/form-feedback'
import { getApiErrorMessage } from '../../services/api/api-client'
import { runContractDryRun, type ContractDryRunReport } from '../../services/api/contract-dry-run-api'
import type { CreateContractRequest } from '../../services/api/contracts-api'

export function ContractDryRunPanel({ contractId, eventTypes, sourceIds, draft, enabled = true, disabled = false }: {
  contractId: string | null; eventTypes: string[]; sourceIds: string[]; draft?: CreateContractRequest; enabled?: boolean; disabled?: boolean
}) {
  const [eventType, setEventType] = useState(eventTypes[0] ?? '')
  const [sourceId, setSourceId] = useState(sourceIds[0] ?? '')
  const [payload, setPayload] = useState('{}')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [result, setResult] = useState<{ fingerprint: string; report: ContractDryRunReport } | null>(null)
  const fingerprint = JSON.stringify({ eventType, sourceId, payload, draft, enabled })
  async function run() {
    setError(null)
    setPending(true)
    try {
      const parsed: unknown = JSON.parse(payload)
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Payload must be a JSON object.')
      const report = await runContractDryRun(contractId, { eventType, sourceId, payload: parsed as Record<string, unknown>, draft, draftEnabled: enabled })
      setResult({ fingerprint, report })
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to run contract simulation.')) }
    finally { setPending(false) }
  }
  const report = result?.fingerprint === fingerprint ? result.report : null
  return <div className="configuration-editor"><ConfigurationSection title="Full dry run" description="Simulate input validation, routing, filters, transformation, output validation and destination configuration. Nothing is saved or sent." icon={<EventsIcon />}>
    <ConfigurationGrid columns={2}>
      <div className="field"><Text id="dry-run-type-label">Event type</Text><Select.Root value={eventType} onValueChange={setEventType}><Select.Trigger aria-label="Dry-run event type" /><Select.Content>{eventTypes.map(type => <Select.Item key={type} value={type}>{type}</Select.Item>)}</Select.Content></Select.Root></div>
      <div className="field"><Text id="dry-run-source-label">Source</Text><Select.Root value={sourceId} onValueChange={setSourceId}><Select.Trigger aria-label="Dry-run source" /><Select.Content>{sourceIds.map(id => <Select.Item key={id} value={id}>{id}</Select.Item>)}</Select.Content></Select.Root></div>
    </ConfigurationGrid>
    <Text as="label" htmlFor="dry-run-payload" size="2">Dry-run payload</Text>
    <TextArea id="dry-run-payload" rows={6} value={payload} onChange={event => setPayload(event.target.value)} />
    <div className="configuration-actions"><Button type="button" variant="soft" disabled={disabled || pending || !eventTypes.includes(eventType) || !sourceIds.includes(sourceId)} onClick={() => void run()}>{pending ? 'Simulating...' : 'Run without sending'}</Button></div>
    {error ? <FormErrorText message={error} /> : null}
    {report ? <Flex direction="column" gap="2">
      <Badge color={report.wouldDeliver ? 'green' : 'amber'}>{report.wouldDeliver ? 'Checks passed' : 'Would not deliver'}</Badge>
      <Text size="2" color="gray">{report.notice}</Text>
      {report.stages.map(stage => <Text key={stage.stage} size="2"><strong>{stage.stage}: {stage.status}</strong> — {stage.message}</Text>)}
      <Text size="1">Input schema: {report.inputSchemaVersion ?? 'none'} · Output schema: {report.outputSchemaVersion ?? 'none'} · Transformation: {report.transformationIdentity ?? 'none'}</Text>
      {report.destinations.map((destination, index) => <Text size="1" key={destination.bindingId ?? destination.destinationId ?? index}>{destination.type}: {destination.status} — {destination.message}</Text>)}
      <pre style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', maxHeight: 320, overflow: 'auto' }}>{JSON.stringify(report.transformedPayload, null, 2)}</pre>
    </Flex> : null}
  </ConfigurationSection></div>
}
