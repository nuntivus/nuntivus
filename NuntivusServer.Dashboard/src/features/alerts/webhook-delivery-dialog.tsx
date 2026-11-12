import { Button, Flex, Table, Text } from '@radix-ui/themes'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { FormDialog } from '../../components/dialogs/form-dialog'
import { FormErrorText } from '../../components/forms/form-feedback'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { apiGet, apiPost, getApiErrorMessage } from '../../services/api/api-client'
import type { PagedResponse } from '../../services/api/paged-response'

type Status = { status: string; attemptCount: number; cycleAttemptCount: number; nextAttemptAt: string | null; leasedUntil: string | null; lastError: string | null; canRetry: boolean }
type Attempt = { attemptNumber: number; startedAt: string; completedAt: string | null; outcome: string; error: string | null }
export function WebhookDeliveryDialog({ id, canWrite, onClose }: { id: string; canWrite: boolean; onClose: () => void }) {
  const [page, setPage] = useState(1)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const operation = useRef<string | null>(null)
  const confirm = useConfirmDialog()
  const queries = useQueryClient()
  const path = `/v1/alerts/notifications/${encodeURIComponent(id)}/webhook`
  const status = useQuery({ queryKey: ['alerts', 'webhook', id], queryFn: () => apiGet<Status>(path), refetchInterval: 5000 })
  const attempts = useQuery({ queryKey: ['alerts', 'webhook', id, 'attempts', page], queryFn: () => apiGet<PagedResponse<Attempt>>(`${path}/attempts`, { page }), refetchInterval: 5000 })
  async function retry() {
    if (!await confirm({ title: 'Retry failed webhook?', description: 'Queues the original notification with a fresh ten-attempt budget. The receiver may already have processed it; its notification ID stays unchanged.', confirmText: 'Retry webhook' })) return
    setPending(true); setError(null)
    operation.current ??= crypto.randomUUID()
    try {
      await apiPost(`${path}/retry`, { operationId: operation.current })
      operation.current = null
      await queries.invalidateQueries({ queryKey: ['alerts', 'webhook', id] })
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to retry webhook.')) }
    finally { setPending(false) }
  }
  return <FormDialog open onOpenChange={open => { if (!open) onClose() }} title="Webhook delivery" description="Operational metadata only. Historical attempts before tracking was enabled have no reconstructed details." maxWidth="800px">
    <Flex direction="column" gap="3">
      <FormErrorText message={error} />
      {status.isError || attempts.isError ? <FormErrorText message="Unable to load webhook delivery history." /> : null}
      {status.data ? <><Text>Status: {status.data.status}</Text><Text>Lifetime attempts: {status.data.attemptCount} · Current retry budget: {status.data.cycleAttemptCount}/10</Text>
        {status.data.nextAttemptAt ? <Text>Next attempt: {status.data.nextAttemptAt}</Text> : null}
        {status.data.leasedUntil ? <Text>Lease expires: {status.data.leasedUntil}</Text> : null}
        {status.data.lastError ? <Text color="red">{status.data.lastError}</Text> : null}
        {canWrite ? <Button disabled={pending || !status.data.canRetry} onClick={() => void retry()}>Retry webhook</Button> : null}</> : <Text>Loading delivery status...</Text>}
      <Table.Root><caption>Webhook attempts</caption><Table.Header><Table.Row>{['Attempt', 'Started', 'Completed', 'Outcome', 'Error'].map(label => <Table.ColumnHeaderCell key={label}>{label}</Table.ColumnHeaderCell>)}</Table.Row></Table.Header>
        <Table.Body>{attempts.data?.items.map(attempt => <Table.Row key={attempt.attemptNumber}><Table.Cell>{attempt.attemptNumber}</Table.Cell><Table.Cell>{attempt.startedAt}</Table.Cell><Table.Cell>{attempt.completedAt ?? '—'}</Table.Cell><Table.Cell>{attempt.outcome}</Table.Cell><Table.Cell>{attempt.error ?? '—'}</Table.Cell></Table.Row>)}</Table.Body></Table.Root>
      {attempts.data && !attempts.data.items.length ? <Text>No recorded attempts.</Text> : null}
      <Flex gap="2"><Button variant="soft" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous attempts</Button><Button variant="soft" disabled={!attempts.data?.hasNext} onClick={() => setPage(page + 1)}>Next attempts</Button></Flex>
    </Flex>
  </FormDialog>
}
