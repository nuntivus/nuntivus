import { Badge, Button, Card, Flex, Heading, Select, Table, Text, TextField } from '@radix-ui/themes'
import { FormDialog } from '../components/dialogs/form-dialog'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { FormErrorText } from '../components/forms/form-feedback'
import { EntitySelect } from '../components/forms/entity-select'
import { ConfigurationGrid, ConfigurationHeading, ConfigurationSection } from '../components/forms/configuration-layout'
import { ContractsIcon, EventsIcon, SettingsIcon } from '../components/icons/nav-icons'
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
  const [createOpen, setCreateOpen] = useState(false)
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
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['alerts'] })
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to save alert rule.')) }
    finally { setPending(false) }
  }
  async function changeCreateOpen(open: boolean) {
    if (pending) return
    if (!open && (!!draft.name || !!draft.contractId || !!draft.webhookUrl || !!draft.webhookSecretRef || draft.signal !== 'DeadLetters' || draft.threshold !== '1' || draft.reminderSeconds !== '0')
      && !await confirm({ title: 'Discard rule changes?', description: 'Your unsaved alert rule will be discarded.', confirmText: 'Discard changes' })) return
    setDraft({ contractId: '', name: '', signal: 'DeadLetters', threshold: '1', reminderSeconds: '0', webhookUrl: '', webhookSecretRef: '' })
    setError(null)
    setCreateOpen(open)
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
    <div className="page-header"><div><h1 className="page-title">Alerts</h1><p className="page-subtitle">Manage alert rules and review contract notifications.</p></div>
      {canWrite ? <Button onClick={() => void changeCreateOpen(true)} disabled={pending}>Add rule</Button> : null}
    </div>
    <FormErrorText message={error} />
    <Card><Flex direction="column" gap="3"><Heading size="4">Rules</Heading>
      <Text size="2" color="gray">To change a rule's contract, signal or threshold, create a replacement rule, then archive the obsolete rule.</Text>
      {rules.isError ? <FormErrorText message={getApiErrorMessage(rules.error, 'Unable to load rules.')} /> : null}
      <Table.Root aria-label="Alert rules">
        <Table.Header><Table.Row>{['Rule name', 'Contract', 'Signal', 'Threshold', 'Reminders', 'Status', 'Enabled', 'Actions'].map(label => <Table.ColumnHeaderCell key={label}>{label}</Table.ColumnHeaderCell>)}</Table.Row></Table.Header>
        <Table.Body>
          {rules.isPending ? <Table.Row><Table.Cell colSpan={8}><Text color="gray">Loading rules...</Text></Table.Cell></Table.Row> : !rules.isError && !rules.data?.items.length ? <Table.Row><Table.Cell colSpan={8}><Text color="gray">No alert rules yet.</Text></Table.Cell></Table.Row> : null}
          {rules.data?.items.map(rule => <Table.Row key={rule.id}>
            <Table.RowHeaderCell>{rule.name}</Table.RowHeaderCell><Table.Cell>{rule.contractId}</Table.Cell><Table.Cell>{rule.signal}</Table.Cell><Table.Cell>{rule.threshold}</Table.Cell><Table.Cell>{rule.reminderSeconds ? `${rule.reminderSeconds}s` : 'Off'}</Table.Cell>
            <Table.Cell><Badge color={rule.archived ? 'gray' : rule.active ? 'amber' : 'green'}>{rule.archived ? 'Archived' : rule.active ? 'Firing' : 'Normal'}</Badge></Table.Cell><Table.Cell>{rule.enabled && !rule.archived ? 'Yes' : 'No'}</Table.Cell>
            <Table.Cell>{!rule.archived ? <Flex gap="2"><Button size="1" variant="soft" disabled={!canWrite || pending} onClick={() => void toggle(rule)}>{rule.enabled ? 'Disable' : 'Enable'}</Button><Button size="1" variant="soft" disabled={!canWrite || pending} onClick={() => void archive(rule)}>Archive</Button></Flex> : null}</Table.Cell>
          </Table.Row>)}
        </Table.Body>
      </Table.Root>
      <Flex align="center" justify="between" gap="3"><Text size="2" color="gray">Page {rulePage}{rules.data ? ` · ${rules.data.totalCount} total` : ''}</Text><Flex gap="2"><Button variant="soft" disabled={rulePage === 1} onClick={() => setRulePage(rulePage - 1)}>Previous rules</Button><Button variant="soft" disabled={!rules.data?.hasNext} onClick={() => setRulePage(rulePage + 1)}>Next rules</Button></Flex></Flex>
    </Flex></Card>
    {webhookId ? <WebhookDeliveryDialog key={webhookId} id={webhookId} canWrite={canWrite} onClose={() => setWebhookId(null)} /> : null}
    <Card><Flex direction="column" gap="3"><Heading size="4">Notifications</Heading>
      {notices.isError ? <FormErrorText message={getApiErrorMessage(notices.error, 'Unable to load notifications.')} /> : null}
      {notices.data?.items.map(notice => <Flex key={notice.id} direction="column" gap="1"><Text><Badge color={notice.state === 'Recovered' ? 'green' : 'amber'}>{notice.state}</Badge> {notice.message}</Text><Text size="1">{notice.createdAt} · {notice.contractId}</Text>
        <Flex gap="2"><Button type="button" size="1" variant="soft" disabled={!canWrite || pending || !!notice.acknowledgedAt} onClick={() => void update(() => apiPost(`/v1/alerts/notifications/${notice.id}/acknowledge`, {}))}>{notice.acknowledgedAt ? 'Acknowledged' : 'Acknowledge'}</Button>
        <Button size="1" variant="soft" onClick={() => setWebhookId(notice.id)}>Webhook delivery</Button></Flex></Flex>)}
      <Flex gap="2"><Button variant="soft" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous notifications</Button><Button variant="soft" disabled={!notices.data?.hasNext} onClick={() => setPage(page + 1)}>Next notifications</Button></Flex>
    </Flex></Card>
    {canWrite ? <FormDialog open={createOpen} onOpenChange={open => void changeCreateOpen(open)} title="Create rule" description="Configure when a contract raises an alert and how operators are notified." maxWidth="960px" footer={<><Button type="button" variant="soft" disabled={pending} onClick={() => void changeCreateOpen(false)}>Cancel</Button><Button type="submit" form="alert-create-form" disabled={pending}>{pending ? 'Creating...' : 'Create alert rule'}</Button></>}><form id="alert-create-form" onSubmit={save} className="settings-editor-form configuration-editor">
      <ConfigurationHeading title="Create rule" description="Configure when a contract raises an alert and how operators are notified." icon={<SettingsIcon />} />
      <ConfigurationSection title="Rule details" description="Choose the contract and a recognizable rule name." icon={<ContractsIcon />}>
        <ConfigurationGrid columns={2}>
          <div className="field"><Text as="label" htmlFor="alert-contractId">Contract ID</Text><EntitySelect id="alert-contractId" kind="contract" aria-label="Contract ID" value={draft.contractId} disabled={pending} onChange={contractId => setDraft({ ...draft, contractId })} /></div>
          <div className="field"><Text as="label" htmlFor="alert-name">Rule name</Text><TextField.Root id="alert-name" value={draft.name} disabled={pending} onChange={event => setDraft({ ...draft, name: event.target.value })} /></div>
        </ConfigurationGrid>
      </ConfigurationSection>
      <ConfigurationSection title="Trigger settings" description="Set the signal, count threshold and reminder interval." icon={<EventsIcon />}>
        <ConfigurationGrid>
          <div className="field"><Text id="alert-signal-label">Alert signal</Text><Select.Root value={draft.signal} onValueChange={signal => setDraft({ ...draft, signal, threshold: signal.startsWith('Slo') ? '0' : '1' })}><Select.Trigger aria-label="Alert signal" aria-labelledby="alert-signal-label" /><Select.Content>{['SloSuccess', 'SloLatency', 'DestinationFailures', 'DeadLetters'].map(signal => <Select.Item key={signal} value={signal}>{signal}</Select.Item>)}</Select.Content></Select.Root></div>
          {([['threshold', 'Count threshold (SLO rules use 0)'], ['reminderSeconds', 'Reminder interval in seconds (0 disables)']] as const).map(([key, label]) => <div className="field" key={key}><Text as="label" htmlFor={`alert-${key}`}>{label}</Text><TextField.Root id={`alert-${key}`} value={draft[key]} disabled={pending} onChange={event => setDraft({ ...draft, [key]: event.target.value })} /></div>)}
        </ConfigurationGrid>
      </ConfigurationSection>
      <ConfigurationSection title="Webhook notifications" description="Optionally deliver notifications to an HTTPS endpoint using a managed credential." icon={<EventsIcon />}>
        <ConfigurationGrid columns={2}>
          {([['webhookUrl', 'HTTPS webhook URL (optional)'], ['webhookSecretRef', 'Managed bearer secret reference (optional)']] as const).map(([key, label]) => <div className="field" key={key}><Text as="label" htmlFor={`alert-${key}`}>{label}</Text><TextField.Root id={`alert-${key}`} value={draft[key]} disabled={pending} onChange={event => setDraft({ ...draft, [key]: event.target.value })} /></div>)}
        </ConfigurationGrid>
      </ConfigurationSection>
      <Text size="1" color="gray">Firing and recovery are always recorded. Reminders use the configured interval. Webhook delivery is retried with a stable notification ID; receivers should deduplicate.</Text>
      <FormErrorText message={error} />
    </form></FormDialog> : null}
  </Flex>
}
