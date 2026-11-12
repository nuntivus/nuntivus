import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createClerkPortalSession } from '../src/features/portal/clerk-session.ts'

function fixture() {
  const updates = []
  const mounts = []
  const client = { session: null, mountSignIn: host => mounts.push(['mount', host]), unmountSignIn: host => mounts.push(['unmount', host]) }
  let local = ''
  const host = {}
  const coordinator = createClerkPortalSession(client, host, () => local, (token, signedIn) => updates.push({ token, signedIn }))
  return { client, coordinator, updates, mounts, setLocal: value => { local = value } }
}

test('Clerk refresh preserves a local session in mixed-provider mode', async () => {
  const f = fixture()
  f.setLocal('local-token')
  await f.coordinator.refresh()
  await f.coordinator.refresh()
  assert.deepEqual(f.updates.at(-1), { token: 'local-token', signedIn: false })
  assert.equal(f.mounts.length, 0)
})

test('Clerk sign-in remounts after logout without duplicate mounts', async () => {
  const f = fixture()
  await f.coordinator.refresh()
  await f.coordinator.refresh()
  f.client.session = { getToken: async () => 'clerk-token' }
  await f.coordinator.refresh()
  assert.deepEqual(f.updates.at(-1), { token: 'clerk-token', signedIn: true })
  f.client.session = null
  await f.coordinator.refresh()
  assert.deepEqual(f.mounts.map(([action]) => action), ['mount', 'unmount', 'mount'])
  f.coordinator.dispose()
  assert.equal(f.mounts.at(-1)[0], 'unmount')
})

test('in-flight Clerk refresh cannot overwrite a new local login', async () => {
  const f = fixture()
  let finish
  f.client.session = { getToken: () => new Promise(resolve => { finish = resolve }) }
  const refresh = f.coordinator.refresh()
  f.setLocal('local-token')
  finish('stale-clerk-token')
  await refresh
  assert.equal(f.updates.length, 0)
})
