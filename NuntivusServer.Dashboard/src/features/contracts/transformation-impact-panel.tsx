import { Button, Flex, Heading, Select, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { FormErrorText } from '../../components/forms/form-feedback'
import { getApiErrorMessage } from '../../services/api/api-client'
import { getTransformationImpact, type ChangeImpactReport } from '../../services/api/change-impact-api'
import type { ContractTransformation } from '../../services/api/contracts-api'
import { ChangeImpactResults } from '../event-definitions/change-impact-panel'

export function TransformationImpactPanel({ contractId, eventTypes, transformation, disabled }: {
  contractId: string; eventTypes: string[]; transformation: ContractTransformation | null; disabled: boolean
}) {
  const [eventType, setEventType] = useState(eventTypes[0] ?? '')
  const [result, setResult] = useState<{ fingerprint: string; report: ChangeImpactReport } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const fingerprint = JSON.stringify({ eventType, transformation })
  async function analyze() {
    setError(null)
    setPending(true)
    try {
      const report = await getTransformationImpact(contractId, eventType, transformation)
      setResult({ fingerprint, report })
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to analyze transformation impact.')) }
    finally { setPending(false) }
  }
  return <Flex direction="column" gap="2">
    <Heading size="3">Transformation impact</Heading>
    <Text size="2" color="gray">Analyze the proposed transformation without saving or delivering anything. Other routing and schema settings use the saved contract.</Text>
    <Select.Root value={eventType} onValueChange={setEventType} disabled={disabled || pending}>
      <Select.Trigger aria-label="Impact event type" />
      <Select.Content>{eventTypes.map(type => <Select.Item key={type} value={type}>{type}</Select.Item>)}</Select.Content>
    </Select.Root>
    <Button type="button" variant="soft" disabled={disabled || pending || !eventTypes.includes(eventType)} onClick={() => void analyze()} style={{ alignSelf: 'flex-start' }}>{pending ? 'Analyzing...' : 'Analyze transformation impact'}</Button>
    {error ? <FormErrorText message={error} /> : null}
    {result && result.fingerprint === fingerprint ? <ChangeImpactResults report={result.report} /> : null}
  </Flex>
}
