import { Button, Card, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { ConfigurationGrid, ConfigurationHeading, ConfigurationSection } from '../../components/forms/configuration-layout'
import { UsersIcon } from '../../components/icons/nav-icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { FormErrorText } from '../../components/forms/form-feedback'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { useUnsavedChangesGuard } from '../../hooks/use-unsaved-changes-guard'
import { apiDelete, apiGet, apiPost, getApiErrorMessage } from '../../services/api/api-client'
import type { PagedResponse } from '../../services/api/paged-response'

type Membership = { accountId: string; customerId: string; issuer: string; subject: string; revoked: boolean }
type Invitation = { id: string; token: string; expiresAt: string }
export function PortalAccessPanel({ customerId }: { customerId: string }) {
  const confirm = useConfirmDialog()
  const queries = useQueryClient()
  const [subject, setSubject] = useState('')
  const [provider, setProvider] = useState('local')
  const [page, setPage] = useState(1)
  const [invitation, setInvitation] = useState<Invitation | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const memberships = useQuery({ queryKey: ['portal-memberships', customerId, page], queryFn: () => apiGet<PagedResponse<Membership>>(`/v1/admin/portal/customers/${encodeURIComponent(customerId)}/memberships`, { page }) })
  useUnsavedChangesGuard({ when: !!subject || !!invitation })
  async function invite(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError(null)
    try { setInvitation(await apiPost<Invitation>('/v1/admin/portal/invitations', { customerId, provider, subject })); setSubject('') }
    catch (cause) { setError(getApiErrorMessage(cause, 'Unable to issue invitation.')) }
    finally { setPending(false) }
  }
  async function revoke(path: string) {
    if (!await confirm({ title: 'Revoke portal access?', description: 'This immediately prevents new requests using this membership or invitation.', confirmText: 'Revoke' })) return
    setPending(true); setError(null)
    try { await apiDelete(path); await queries.invalidateQueries({ queryKey: ['portal-memberships', customerId] }); setInvitation(null) }
    catch (cause) { setError(getApiErrorMessage(cause, 'Unable to revoke access.')) }
    finally { setPending(false) }
  }
  return <Card className="configuration-editor"><Flex direction="column" gap="3"><ConfigurationHeading title="Customer portal access" description="Manage invitation-only access to contracts and delivery history." icon={<UsersIcon />} />
    <Text size="2">Invitation-only access to contracts and delivery history. Local subjects are exact usernames; Clerk subjects are the provider user IDs, not email addresses.</Text>
    <FormErrorText message={error} />{memberships.isError ? <FormErrorText message="Unable to load memberships." /> : null}
    {memberships.data?.items.map(member => <Flex gap="2" key={member.accountId} align="center" wrap="wrap"><Text>{member.subject} · {member.issuer} · {member.revoked ? 'Revoked' : 'Active'}</Text><Button size="1" variant="soft" disabled={pending || member.revoked} onClick={() => void revoke(`/v1/admin/portal/customers/${encodeURIComponent(customerId)}/memberships/${member.accountId}`)}>Revoke membership</Button></Flex>)}
    <Flex gap="2"><Button variant="soft" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous memberships</Button><Button variant="soft" disabled={!memberships.data?.hasNext} onClick={() => setPage(page + 1)}>Next memberships</Button></Flex>
    <form onSubmit={invite} className="settings-editor-form"><ConfigurationSection title="Issue invitation" description="Choose the identity provider and the exact subject to invite." icon={<UsersIcon />}>
      <ConfigurationGrid columns={2}>
        <div className="field"><Text as="label" htmlFor="portal-subject">Invited subject</Text><TextField.Root id="portal-subject" value={subject} onChange={event => setSubject(event.target.value)} disabled={pending} /></div>
        <div className="field"><Text id="portal-provider-label">Identity provider</Text><Select.Root value={provider} onValueChange={setProvider}><Select.Trigger aria-label="Identity provider" aria-labelledby="portal-provider-label" /><Select.Content><Select.Item value="local">Local</Select.Item><Select.Item value="clerk">Clerk</Select.Item></Select.Content></Select.Root></div>
      </ConfigurationGrid>
      <div className="configuration-actions"><Button type="submit" disabled={pending || !subject.trim()}>Issue invitation</Button></div>
    </ConfigurationSection></form>
    {invitation ? <Flex direction="column" gap="2"><Text>Share this one-time token privately with the invited user. Open /portal to redeem it. Expires {invitation.expiresAt}. Creating a local recovery invitation allows resetting the account password.</Text><TextField.Root aria-label="One-time invitation token" readOnly value={invitation.token} /><Flex gap="2"><Button variant="soft" disabled={pending} onClick={() => void revoke(`/v1/admin/portal/invitations/${invitation.id}`)}>Revoke invitation</Button><Button variant="soft" onClick={() => setInvitation(null)}>Dismiss token</Button></Flex></Flex> : null}
  </Flex></Card>
}
