import { Badge, Button, Card, Flex, Heading, Select, Text, TextField } from '@radix-ui/themes'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { FormErrorText } from '../components/forms/form-feedback'
import { hasPermission } from '../auth/permissions'
import { useAppSelector } from '../store/hooks'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { apiDelete, apiGet, apiPost, apiPut, getApiErrorMessage } from '../services/api/api-client'
import { WebhookDeliveryDialog } from '../features/alerts/webhook-delivery-dialog'
import type { PagedResponse } from '../services/api/paged-response'

type Rule = { id: string; contractId: string; name: string; signal: string; threshold: number; reminderSeconds: number; enabled: boolean; active: boolean; webhookUrl: string | null; webhookSecretRef: string | null; archived: boolean }
type Notice = { id: string; contractId: string; state: string; message: string; createdAt: string; acknowledgedAt: string | null }
export function AlertsPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirmDialog()
  const user = useAppSelector(state => state.auth.currentUser)
  const canWrite = hasPermission(user, 'contracts.write')
  const [page, setPage] = useState(1)
  const [rulePage, setRulePage] = useState(1)
  const [draft, setDraft] = useState({ contractId: '', name: '', signal: 'DeadLetters', threshold: '1', reminderSeconds: '0', webhookUrl: '', webhookSecretRef: '' })
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [webhookId, setWebhookId] = useState<string | null>(null)
  const rules = useQuery({ queryKey: ['alerts', 'rules', rulePage], queryFn: () => apiGet<PagedResponse<Rule>>('/v1/alerts/rules', { page: rulePage }) })
  const notices = useQuery({ queryKey: ['alerts', 'notifications', page], queryFn: () => apiGet<PagedResponse<Notice>>('/v1/alerts/notifications', { page }), refetchInterval: 30000 })
  useUnsavedChangesGuard({ when: !!draft.name || !!draft.contractId || !!draft.webhookUrl || !!draft.webhookSecretRef || draft.signal !== 'DeadLetters' || draft.threshold !== '1' || draft.reminderSeconds !== '0' })
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    if (!draft.contractId || !draft.name.trim()) { setError('Contract ID and rule name are required.'); return }
    setPending(true)
    try {
      await apiPut(`/v1/alerts/rules/${crypto.randomUUID()}`, { ...draft, threshold: Number(draft.threshold), reminderSeconds: Number(draft.reminderSeconds), enabled: true, webhookUrl: draft.webhookUrl || null, webhookSecretRef: draft.webhookSecretRef || null })
      setDraft({ contractId: '', name: '', signal: 'DeadLetters', threshold: '1', reminderSeconds: '0', webhookUrl: '', webhookSecretRef: '' })
      await queryClient.invalidateQueries({ queryKey: ['alerts'] })
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to save alert rule.')) }
    finally { setPending(false) }
  }
  async function toggle(rule: Rule) {
    if (!await confirm({ title: rule.enabled ? 'Disable alert rule?' : 'Enable alert rule?', description: 'Disabling pauses evaluation. Existing notifications and queued webhooks are retained.', confirmText: rule.enabled ? 'Disable rule' : 'Enable rule' })) return
    await update(() => apiPut(`/v1/alerts/rules/${rule.id}`, { ...rule, enabled: !rule.enabled }))
  }
  async function archive(rule: Rule) {
    if (!await confirm({ title: 'Archive alert rule?', description: 'Stops future evaluation permanently. Notifications, attempt history and queued webhook sends are retained. Create a new rule to replace its signal or threshold.', confirmText: 'Archive rule' })) return
    await update(() => apiDelete(`/v1/alerts/rules/${rule.id}`))
  }
  async function update(action: () => Promise<unknown>) {
    setError(null)
    setPending(true)
    try { await action(); await queryClient.invalidateQueries({ queryKey: ['alerts'] }) }
    catch (cause) { setError(getApiErrorMessage(cause, 'Unable to update alert.')) }
    finally { setPending(false) }
  }
  return <Flex direction="column" gap="4">
    <Heading>Alerts</Heading>
    <FormErrorText message={error} />
    {webhookId ? <WebhookDeliveryDialog key={webhookId} id={webhookId} canWrite={canWrite} onClose={() => setWebhookId(null)} /> : null}
    <Card><Flex direction="column" gap="3"><Heading size="4">Notifications</Heading>
      {notices.isError ? <FormErrorText message={getApiErrorMessage(notices.error, 'Unable to load notifications.')} /> : null}
      {notices.data?.items.map(notice => <Flex key={notice.id} direction="column" gap="1"><Text><Badge color={notice.state === 'Recovered' ? 'green' : 'amber'}>{notice.state}</Badge> {notice.message}</Text><Text size="1">{notice.createdAt} · {notice.contractId}</Text>
        <Flex gap="2"><Button type="button" size="1" variant="soft" disabled={!canWrite || pending || !!notice.acknowledgedAt} onClick={() => void update(() => apiPost(`/v1/alerts/notifications/${notice.id}/acknowledge`, {}))}>{notice.acknowledgedAt ? 'Acknowledged' : 'Acknowledge'}</Button>
        <Button size="1" variant="soft" onClick={() => setWebhookId(notice.id)}>Webhook delivery</Button></Flex></Flex>)}
      <Flex gap="2"><Button variant="soft" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous notifications</Button><Button variant="soft" disabled={!notices.data?.hasNext} onClick={() => setPage(page + 1)}>Next notifications</Button></Flex>
    </Flex></Card>
    <Card><Flex direction="column" gap="3"><Heading size="4">Rules</Heading>
      {rules.isError ? <FormErrorText message={getApiErrorMessage(rules.error, 'Unable to load rules.')} /> : null}
      <Text size="2" color="gray">To change a rule's contract, signal or threshold, create a replacement rule, then archive the obsolete rule.</Text>
      {rules.data?.items.map(rule => <Flex key={rule.id} align="center" gap="2" wrap="wrap"><Text>{rule.name} · {rule.signal} · {rule.archived ? 'Archived' : rule.active ? 'Firing' : 'Normal'} · reminders {rule.reminderSeconds ? `${rule.reminderSeconds}s` : 'off'}</Text>
        {!rule.archived ? <><Button size="1" variant="soft" disabled={!canWrite || pending} onClick={() => void toggle(rule)}>{rule.enabled ? 'Disable' : 'Enable'}</Button>
        <Button size="1" variant="soft" disabled={!canWrite || pending} onClick={() => void archive(rule)}>Archive</Button></> : null}</Flex>)}
      <Flex gap="2"><Button variant="soft" disabled={rulePage === 1} onClick={() => setRulePage(rulePage - 1)}>Previous rules</Button><Button variant="soft" disabled={!rules.data?.hasNext} onClick={() => setRulePage(rulePage + 1)}>Next rules</Button></Flex>
    </Flex></Card>
    {canWrite ? <Card><form onSubmit={save}><Flex direction="column" gap="3" style={{ maxWidth: 560 }}><Heading size="4">Create rule</Heading>
      {([['contractId', 'Contract ID'], ['name', 'Rule name'], ['threshold', 'Count threshold (SLO rules use 0)'], ['reminderSeconds', 'Reminder interval in seconds (0 disables)'], ['webhookUrl', 'HTTPS webhook URL (optional)'], ['webhookSecretRef', 'Managed bearer secret reference (optional)']] as const).map(([key, label]) => <Flex direction="column" gap="1" key={key}><Text as="label" htmlFor={`alert-${key}`}>{label}</Text><TextField.Root id={`alert-${key}`} value={draft[key]} disabled={pending} onChange={event => setDraft({ ...draft, [key]: event.target.value })} /></Flex>)}
      <Select.Root value={draft.signal} onValueChange={signal => setDraft({ ...draft, signal, threshold: signal.startsWith('Slo') ? '0' : '1' })}><Select.Trigger aria-label="Alert signal" /><Select.Content>{['SloSuccess', 'SloLatency', 'DestinationFailures', 'DeadLetters'].map(signal => <Select.Item key={signal} value={signal}>{signal}</Select.Item>)}</Select.Content></Select.Root>
      <Text size="1" color="gray">Firing and recovery are always recorded. Reminders use the configured interval. Webhook delivery is retried with a stable notification ID; receivers should deduplicate.</Text>
      <Button type="submit" disabled={pending}>Create alert rule</Button>
    </Flex></form></Card> : null}
  </Flex>
}
