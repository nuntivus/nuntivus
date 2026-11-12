import { Button, Card, Flex, Heading, Select, Table, Text, TextField } from '@radix-ui/themes'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { Clerk } from '@clerk/clerk-js'
import { FormErrorText } from '../components/forms/form-feedback'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../services/api/api-client'
import { portalGet, portalPost, portalSessionKey } from '../services/api/portal-api'
import type { PagedResponse } from '../services/api/paged-response'
import { createClerkPortalSession } from '../features/portal/clerk-session'

type Customer = { id: string; name: string }
type Contract = { id: string; customerId: string; enabled: boolean; eventTypes: string[] }
type Delivery = { id: string; contractId: string; eventType: string; status: string; attemptCount: number }
type Configuration = { localEnabled: boolean; clerkEnabled: boolean }

export function PortalPage() {
  const [token, setToken] = useState(() => sessionStorage.getItem(portalSessionKey) ?? '')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [invitation, setInvitation] = useState('')
  const [customer, setCustomer] = useState('')
  const [page, setPage] = useState(1)
  const [contractPage, setContractPage] = useState(1)
  const [customerPage, setCustomerPage] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [clerkSignedIn, setClerkSignedIn] = useState(false)
  const clerk = useRef<Clerk | null>(null)
  const localToken = useRef(sessionStorage.getItem(`${portalSessionKey}.provider`) === 'clerk' ? '' : token)
  const clerkSession = useRef<ReturnType<typeof createClerkPortalSession> | null>(null)
  const signInHost = useRef<HTMLDivElement>(null)
  const config = useQuery({ queryKey: ['portal-configuration'], queryFn: () => portalGet<Configuration>('configuration', '') })
  const customers = useQuery({ queryKey: ['portal', token, 'customers', customerPage], queryFn: () => portalGet<PagedResponse<Customer>>('customers', token, { page: customerPage }), enabled: !!token, retry: false })
  const selected = customer || customers.data?.items[0]?.id || ''
  const contracts = useQuery({ queryKey: ['portal', token, selected, 'contracts', contractPage], queryFn: () => portalGet<PagedResponse<Contract>>(`customers/${encodeURIComponent(selected)}/contracts`, token, { page: contractPage }), enabled: !!token && !!selected, retry: false })
  const deliveries = useQuery({ queryKey: ['portal', token, selected, 'deliveries', page], queryFn: () => portalGet<PagedResponse<Delivery>>(`customers/${encodeURIComponent(selected)}/deliveries`, token, { page }), enabled: !!token && !!selected, retry: false, refetchInterval: 30000 })
  useUnsavedChangesGuard({ when: !!password || !!invitation || !!username })
  function session(value: string) { localToken.current = value; setToken(value); sessionStorage.setItem(portalSessionKey, value); sessionStorage.setItem(`${portalSessionKey}.provider`, value ? 'local' : ''); setCustomer(''); setPage(1); setContractPage(1); setCustomerPage(1) }
  useEffect(() => {
    const publishableKey = import.meta.env.VITE_CLERK_PUBLISHABLE_KEY as string | undefined
    if (!config.data?.clerkEnabled || !publishableKey) return
    let disposed = false
    let unsubscribe: (() => void) | undefined
    let timer: ReturnType<typeof setInterval> | undefined
    const host = signInHost.current
    void (async () => {
      const { Clerk } = await import('@clerk/clerk-js')
      const client = new Clerk(publishableKey)
      await client.load()
      if (disposed) return
      clerk.current = client
      const coordinator = createClerkPortalSession(client, host, () => localToken.current, (value, signedIn) => {
        if (disposed) return
        setToken(value); setClerkSignedIn(signedIn); sessionStorage.setItem(portalSessionKey, value)
        sessionStorage.setItem(`${portalSessionKey}.provider`, localToken.current ? 'local' : signedIn ? 'clerk' : '')
      })
      clerkSession.current = coordinator
      const refresh = () => coordinator.refresh()
      unsubscribe = client.addListener(() => { void refresh().catch(() => { if (!disposed) setError('Unable to refresh Clerk session.') }) })
      await refresh()
      timer = setInterval(() => { void refresh().catch(() => { if (!disposed) setError('Unable to refresh Clerk session.') }) }, 30000)
    })().catch(() => { if (!disposed) setError('Unable to initialize Clerk sign-in.') })
    return () => { disposed = true; unsubscribe?.(); if (timer) clearInterval(timer); clerkSession.current?.dispose(); clerkSession.current = null }
  }, [config.data?.clerkEnabled])
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError(null)
    try {
      if (clerkSignedIn) { await portalPost('invitations/accept', { token: invitation }, token); setInvitation(''); await customers.refetch() }
      else {
        const result = await portalPost<{ accessToken: string }>(invitation ? 'local/accept' : 'local/login', invitation ? { token: invitation, password } : { username, password })
        session(result.accessToken); await clerkSession.current?.refresh(); setPassword(''); setUsername(''); setInvitation('')
      }
    } catch (cause) { setError(getApiErrorMessage(cause, 'Unable to sign in or accept invitation.')) }
    finally { setPending(false) }
  }
  async function logout() {
    setError(null)
    try {
      if (!localToken.current && clerk.current?.session) await clerk.current.signOut()
      session(''); setClerkSignedIn(false); await clerkSession.current?.refresh()
    } catch { setError('Unable to sign out. Please try again.') }
  }
  return <Flex direction="column" gap="4" style={{ maxWidth: 1000, margin: '32px auto', padding: 16 }}>
    <Heading>Customer portal</Heading><Text color="gray">Invitation-only, read-only contracts and delivery history.</Text>
    <FormErrorText message={error} />
    {config.isError ? <FormErrorText message="Unable to load portal authentication options." /> : null}
    {config.data?.clerkEnabled ? <><div ref={signInHost} />{!import.meta.env.VITE_CLERK_PUBLISHABLE_KEY ? <Text>Clerk sign-in requires the portal publishable key configuration.</Text> : null}</> : null}
    {(!token && config.data?.localEnabled || clerkSignedIn) ? <Card><form onSubmit={submit}><Flex direction="column" gap="3" style={{ maxWidth: 480 }}>
      {!clerkSignedIn ? <><Text as="label" htmlFor="portal-username">Username</Text><TextField.Root id="portal-username" autoComplete="username" value={username} onChange={event => setUsername(event.target.value)} />
      <Text as="label" htmlFor="portal-password">Password</Text><TextField.Root id="portal-password" type="password" autoComplete={invitation ? 'new-password' : 'current-password'} value={password} onChange={event => setPassword(event.target.value)} /></> : null}
      <Text as="label" htmlFor="portal-invitation">Invitation token (only for enrollment or recovery)</Text><TextField.Root id="portal-invitation" type="password" autoComplete="off" value={invitation} onChange={event => setInvitation(event.target.value)} />
      <Button type="submit" disabled={pending}>{invitation || clerkSignedIn ? 'Accept invitation' : 'Sign in'}</Button>
      <Text size="1" color="gray">Ask an administrator for access or local password recovery. Invitations expire after two days.</Text>
    </Flex></form></Card> : null}
    {token ? <><Button variant="soft" style={{ alignSelf: 'flex-start' }} onClick={() => void logout()}>Sign out</Button>
      {customers.isError || contracts.isError || deliveries.isError ? <FormErrorText message="Unable to load portal data. Your session may have expired or access may have been revoked." /> : null}
      {!customers.isPending && !customers.data?.items.length ? <Text>No active customer memberships. Ask an administrator for an invitation.</Text> : null}
      {customers.data?.items.length ? <><Select.Root value={selected} onValueChange={value => { setCustomer(value); setPage(1); setContractPage(1) }}><Select.Trigger aria-label="Customer account" /><Select.Content>{customers.data.items.map(item => <Select.Item key={item.id} value={item.id}>{item.name}</Select.Item>)}</Select.Content></Select.Root>
        <Flex gap="2"><Button variant="soft" disabled={customerPage === 1} onClick={() => { setCustomer(''); setCustomerPage(customerPage - 1) }}>Previous accounts</Button><Button variant="soft" disabled={!customers.data.hasNext} onClick={() => { setCustomer(''); setCustomerPage(customerPage + 1) }}>Next accounts</Button></Flex></> : null}
      <Heading size="4">Contracts</Heading><Table.Root><Table.Header><Table.Row><Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Event types</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Enabled</Table.ColumnHeaderCell></Table.Row></Table.Header><Table.Body>{contracts.data?.items.map(item => <Table.Row key={item.id}><Table.Cell>{item.id}</Table.Cell><Table.Cell>{item.eventTypes.join(', ')}</Table.Cell><Table.Cell>{item.enabled ? 'Yes' : 'No'}</Table.Cell></Table.Row>)}</Table.Body></Table.Root>
      <Flex gap="2"><Button variant="soft" disabled={contractPage === 1} onClick={() => setContractPage(contractPage - 1)}>Previous contracts</Button><Button variant="soft" disabled={!contracts.data?.hasNext} onClick={() => setContractPage(contractPage + 1)}>Next contracts</Button></Flex>
      <Heading size="4">Delivery history</Heading><Table.Root><Table.Header><Table.Row><Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Event</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell></Table.Row></Table.Header><Table.Body>{deliveries.data?.items.map(item => <Table.Row key={item.id}><Table.Cell>{item.id}</Table.Cell><Table.Cell>{item.contractId}</Table.Cell><Table.Cell>{item.eventType}</Table.Cell><Table.Cell>{item.status}</Table.Cell><Table.Cell>{item.attemptCount}</Table.Cell></Table.Row>)}</Table.Body></Table.Root>
      <Flex gap="2"><Button variant="soft" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous deliveries</Button><Button variant="soft" disabled={!deliveries.data?.hasNext} onClick={() => setPage(page + 1)}>Next deliveries</Button></Flex>
    </> : null}
  </Flex>
}
