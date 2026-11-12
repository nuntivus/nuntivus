import { Button, Card, Flex, Heading, Link, Select, Text, TextArea } from '@radix-ui/themes'
import { useRef, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { hasPermission } from '../../auth/permissions'
import { FormErrorText } from '../../components/forms/form-feedback'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { ApiError, getApiErrorMessage } from '../../services/api/api-client'
import {
  previewContractTestEvent,
  sendContractTestEvent,
  type ContractDetail,
  type ContractTestEventRequest,
  type ContractTestPreview,
  type ContractTestSend,
} from '../../services/api/contracts-api'
import { useAppSelector } from '../../store/hooks'
import { ContractDryRunPanel } from './contract-dry-run-panel'

function parseObject(value: string, field: string): Record<string, unknown> {
  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    throw new Error(`${field} must be valid JSON.`)
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`${field} must be a JSON object.`)
  }
  return parsed as Record<string, unknown>
}

export function ContractTestEventForm({ contract }: { contract: ContractDetail }) {
  const [eventType, setEventType] = useState(contract.eventTypes[0] ?? '')
  const [payload, setPayload] = useState('{}')
  const [metadata, setMetadata] = useState('{}')
  const [preview, setPreview] = useState<ContractTestPreview | null>(null)
  const [result, setResult] = useState<ContractTestSend | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const inputVersion = useRef(0)
  const { currentUser } = useAppSelector((state) => state.auth)
  const canSend = hasPermission(currentUser, 'events.publish')
  const confirm = useConfirmDialog()
  const queryClient = useQueryClient()

  function invalidatePreview() {
    inputVersion.current += 1
    setPreview(null)
    setResult(null)
    setError(null)
  }

  function buildRequest(): ContractTestEventRequest {
    if (!eventType) {
      throw new Error('Select an event type.')
    }
    const metadataObject = parseObject(metadata, 'Metadata')
    if (Object.values(metadataObject).some((value) => typeof value !== 'string')) {
      throw new Error('Metadata values must be strings.')
    }
    return {
      type: eventType,
      payload: parseObject(payload, 'Payload'),
      metadata: metadataObject as Record<string, string>,
    }
  }

  async function handlePreview() {
    setError(null)
    setPreview(null)
    setResult(null)
    const version = inputVersion.current
    try {
      const request = buildRequest()
      setPending(true)
      const match = await previewContractTestEvent(contract.id, request)
      if (version === inputVersion.current) {
        setPreview(match)
      }
    } catch (cause) {
      if (version === inputVersion.current) {
        setError(cause instanceof Error && !(cause instanceof ApiError)
          ? cause.message
          : getApiErrorMessage(cause, 'Unable to preview test event.'))
      }
    } finally {
      setPending(false)
    }
  }

  async function handleSend() {
    if (!preview?.matches || !canSend || pending) {
      return
    }

    const accepted = await confirm({
      title: 'Send real test event?',
      description: `This will deliver a test event to the configured ${contract.destinationType} destination for this contract only. The destination may perform real actions.`,
      confirmText: 'Send test event',
      cancelText: 'Cancel',
      confirmColor: 'red',
    })
    if (!accepted) {
      return
    }

    setError(null)
    try {
      const request = buildRequest()
      setPending(true)
      const sent = await sendContractTestEvent(contract.id, request)
      setPreview(null)
      setResult(sent)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['contracts', 'detail', contract.id, 'deliveries'] }),
        queryClient.invalidateQueries({ queryKey: ['events'] }),
        queryClient.invalidateQueries({ queryKey: ['deliveries'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ])
    } catch (cause) {
      setError(cause instanceof Error && !(cause instanceof ApiError)
        ? cause.message
        : getApiErrorMessage(cause, 'Unable to send test event.'))
    } finally {
      setPending(false)
    }
  }

  return (
    <Card>
      <Flex direction="column" gap="3">
        <Heading size="4">Test {contract.name ?? contract.id}</Heading>
        <Text size="2" color="gray">
          Preview checks this contract only. Sending creates a real delivery to its configured destination; it will not match other contracts.
        </Text>
        <Text size="2" as="label" htmlFor="test-event-type">Event type</Text>
        <Select.Root value={eventType} onValueChange={(value) => { setEventType(value); invalidatePreview() }}>
          <Select.Trigger id="test-event-type" placeholder="Select an event type" />
          <Select.Content>
            {contract.eventTypes.map((type) => <Select.Item key={type} value={type}>{type}</Select.Item>)}
          </Select.Content>
        </Select.Root>
        <Text size="2" as="label" htmlFor="test-event-payload">Payload (JSON object)</Text>
        <TextArea id="test-event-payload" value={payload} rows={8} onChange={(event) => { setPayload(event.target.value); invalidatePreview() }} />
        <Text size="2" as="label" htmlFor="test-event-metadata">Metadata (JSON object of strings)</Text>
        <TextArea id="test-event-metadata" value={metadata} rows={3} onChange={(event) => { setMetadata(event.target.value); invalidatePreview() }} />
        <Flex gap="2" wrap="wrap">
          <Button type="button" variant="soft" disabled={pending} onClick={() => { void handlePreview() }}>
            Preview match
          </Button>
          <Button type="button" color="red" disabled={pending || !preview?.matches || !canSend} onClick={() => { void handleSend() }}>
            Send test event
          </Button>
        </Flex>
        {!canSend ? <Text size="2" color="gray">Operator permission is required to send test events.</Text> : null}
        {error ? <FormErrorText message={error} /> : null}
        {preview ? (
          <Text size="2" color={preview.matches ? 'green' : 'amber'}>
            {preview.matches ? 'Match' : 'No match'}: {preview.reason}
          </Text>
        ) : null}
        {preview?.matches && 'transformedPayload' in preview ? <Flex direction="column" gap="2">
          <Text size="2" weight="medium">Prepared output payload</Text>
          <Text size="1" color="gray">Output schema: {preview.outputSchemaVersion ?? 'not configured'} · Transformation: {preview.transformationIdentity ?? 'none'}</Text>
          <pre style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', maxHeight: 320, overflow: 'auto' }}>{JSON.stringify(preview.transformedPayload, null, 2)}</pre>
        </Flex> : null}
        {result ? (
          <Flex direction="column" gap="2">
            <Text size="2" color="green">Test event queued for this contract only.</Text>
            <Link asChild><NavLink to={`/events/${encodeURIComponent(result.eventId)}`}>View event</NavLink></Link>
            <Link asChild><NavLink to={`/deliveries/${encodeURIComponent(result.deliveryId)}`}>View delivery and attempts</NavLink></Link>
          </Flex>
        ) : null}
        <ContractDryRunPanel contractId={contract.id} eventTypes={contract.eventTypes} sourceIds={contract.sourceIds} />
      </Flex>
    </Card>
  )
}
