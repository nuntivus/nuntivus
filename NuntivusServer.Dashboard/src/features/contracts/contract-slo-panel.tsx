import { Button, Card, Flex, Heading, Text, TextField } from '@radix-ui/themes'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { FieldErrorText, FormErrorText } from '../../components/forms/form-feedback'
import { ConfigurationGrid, ConfigurationHeading } from '../../components/forms/configuration-layout'
import { SettingsIcon } from '../../components/icons/nav-icons'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { useUnsavedChangesGuard } from '../../hooks/use-unsaved-changes-guard'
import { ApiError, getApiErrorMessage } from '../../services/api/api-client'
import { configureContractSlo, getContractSlo, type ContractSloBudget, type ContractSloPolicy } from '../../services/api/contract-slo-api'

function Budget({ title, value }: { title: string; value: ContractSloBudget }) {
  return <Flex direction="column" gap="1">
    <Heading size="3">{title}</Heading>
    <Text>{value.attainmentPercent === null ? 'No eligible traffic' : `${value.attainmentPercent.toFixed(3)}% attainment`}</Text>
    <Text size="2">{value.good} good / {value.eligible} eligible · {value.bad} bad · {value.pending} pending</Text>
    <Text size="2">Error budget: {value.remainingFailures.toFixed(3)} remaining of {value.allowedFailures.toFixed(3)} allowed failures</Text>
  </Flex>
}

export function ContractSloPanel({ contractId, canWrite }: { contractId: string; canWrite: boolean }) {
  const queryClient = useQueryClient()
  const confirm = useConfirmDialog()
  const [draft, setDraft] = useState<Record<keyof ContractSloPolicy, string> | null>(null)
  const [attempted, setAttempted] = useState(false)
  const query = useQuery({ queryKey: ['contracts', contractId, 'slo'], queryFn: () => getContractSlo(contractId), retry: false })
  const missing = query.error instanceof ApiError && query.error.status === 404
  const mutation = useMutation({ mutationFn: (policy: ContractSloPolicy) => configureContractSlo(contractId, policy),
    onSuccess: async report => { queryClient.setQueryData(['contracts', contractId, 'slo'], report); setDraft(null); setAttempted(false) } })
  useUnsavedChangesGuard({ when: draft !== null })
  const policy = draft ? Object.fromEntries(Object.entries(draft).map(([key, value]) => [key, Number(value)])) as unknown as ContractSloPolicy : null
  const invalid = policy && (!Object.values(policy).every(Number.isFinite) || policy.successTarget <= 0 || policy.successTarget > 100
    || policy.latencyTarget <= 0 || policy.latencyTarget > 100 || !Number.isInteger(policy.latencyThresholdSeconds) || !Number.isInteger(policy.successDeadlineSeconds)
    || policy.latencyThresholdSeconds < 1 || policy.successDeadlineSeconds > 604800 || policy.latencyThresholdSeconds > policy.successDeadlineSeconds)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setAttempted(true)
    if (!policy || invalid || !canWrite) return
    if (query.data && !await confirm({ title: 'Change SLO policy?', description: 'Changing a policy starts a new monitoring period. Existing observations are retained, but this report will no longer claim a full rolling window.', confirmText: 'Save policy' })) return
    try { await mutation.mutateAsync(policy) } catch { /* Mutation error is rendered below. */ }
  }
  return <Card className="contract-wide-panel configuration-editor"><Flex direction="column" gap="3">
    <ConfigurationHeading title="Contract SLOs · rolling 30 days" description="Configure success and latency targets for delivery monitoring." icon={<SettingsIcon />} />
    {query.isPending ? <Text>Loading SLOs...</Text> : missing ? <Text color="gray">No SLO policy configured.</Text> : query.isError ? <FormErrorText message={getApiErrorMessage(query.error, 'Unable to load SLOs.')} /> : query.data ? <>
      <Text size="2" color="gray">{query.data.fullWindowCovered ? 'Full window covered' : 'Partial monitoring window'} · since {query.data.windowStart}</Text>
      <Flex gap="5" wrap="wrap"><Budget title="Success" value={query.data.success} /><Budget title="Latency" value={query.data.latency} /></Flex>
      <Text size="1" color="gray">{query.data.notice}</Text>
      <Text size="1">{query.data.excluded} excluded observations</Text>
    </> : null}
    {canWrite && !draft ? <Button type="button" variant="soft" style={{ alignSelf: 'flex-start' }} onClick={() => {
      const current = query.data?.policy ?? { successTarget: 99.9, latencyTarget: 99, latencyThresholdSeconds: 60, successDeadlineSeconds: 86400 }
      setDraft(Object.fromEntries(Object.entries(current).map(([key, value]) => [key, String(value)])) as Record<keyof ContractSloPolicy, string>)
    }}>Configure SLOs</Button> : null}
    {draft ? <form onSubmit={submit}><Flex direction="column" gap="3">
      <ConfigurationGrid columns={2}>
      {([['successTarget', 'Success target (%)'], ['latencyTarget', 'Latency target (%)'], ['latencyThresholdSeconds', 'Latency threshold (seconds)'], ['successDeadlineSeconds', 'Success deadline (seconds)']] as const).map(([key, label]) => <Flex direction="column" gap="1" key={key}>
        <Text as="label" htmlFor={`slo-${key}`}>{label}</Text><TextField.Root id={`slo-${key}`} type="number" value={draft[key]} onChange={event => setDraft({ ...draft, [key]: event.target.value })} disabled={mutation.isPending} />
      </Flex>)}
      </ConfigurationGrid>
      <Text size="1" color="gray">Tests and manual event replays are excluded. Each destination is measured separately. Default success deadline: 24 hours.</Text>
      <FieldErrorText id="slo-policy-error" message={attempted && invalid ? 'Use targets above 0 up to 100 and integer deadlines from 1 to 604800 seconds, with latency no greater than the success deadline.' : null} />
      <FormErrorText message={mutation.isError ? getApiErrorMessage(mutation.error, 'Unable to save SLO policy.') : null} />
      <Flex gap="2"><Button type="submit" disabled={mutation.isPending}>Save policy</Button><Button type="button" variant="soft" disabled={mutation.isPending} onClick={() => { setDraft(null); setAttempted(false) }}>Cancel</Button></Flex>
    </Flex></form> : null}
  </Flex></Card>
}
