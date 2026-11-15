import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const timestamp = '2026-01-01T00:00:00Z'

async function openApp() {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  const writes = []
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('**/v1/**', async route => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    let body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
    if (method !== 'GET') writes.push({ path, body: route.request().postDataJSON() })
    if (path === '/v1/admin/users/me') {
      body = { subject: 'test-admin', roles: ['Admin'], permissions: [
        'users.read', 'roles.manage', 'customers.read', 'customers.write',
        'event-definitions.read', 'event-definitions.write', 'events.read', 'events.replay',
      ] }
    } else if (path === '/v1/admin/roles') {
      body = { roles: [{ name: 'viewer', description: 'Read only', permissions: [] }] }
    } else if (path === '/v1/admin/users') {
      body = []
    } else if (path === '/v1/admin/users/empty-roles') {
      body = { subject: 'empty-roles', displayName: 'No roles', roles: [], createdAt: timestamp, updatedAt: timestamp }
    } else if (path === '/v1/event-definitions/orders.created') {
      body = { eventType: 'orders.created', displayName: 'Order created', description: null, createdAt: timestamp }
    } else if (path === '/v1/event-definitions/orders.created/schemas' && method === 'POST') {
      body = { ...route.request().postDataJSON(), eventType: 'orders.created', state: 'Draft', createdAt: timestamp }
    } else if (path === '/v1/events/event-1') {
      body = { id: 'event-1', idempotencyKey: 'key-1', type: 'orders.created', source: null,
        occurredAt: timestamp, receivedAt: timestamp, payload: {}, metadata: {}, deliveries: [],
        schemaValidationStatus: 'NotConfigured', inputSchemaVersion: null }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page, writes }
}

for (const scenario of [
  { path: '/users', open: 'New user', title: 'Invite or create user', submit: 'Create user',
    errors: ['Subject is required.'], fields: [['Subject', 'test-user']] },
  { path: '/event-definitions', open: 'New definition', title: 'Create definition', submit: 'Create definition',
    errors: ['Event type is required.', 'Display name is required.'], fields: [['Event type', 'orders.created'], ['Display name', 'Order created']] },
  { path: '/customers', open: 'New customer', title: 'Create customer', submit: 'Create customer',
    errors: ['Customer id is required.', 'Customer name is required.'], fields: [['Customer id', 'customer-1'], ['Customer name', 'Customer One']] },
]) {
  test(`${scenario.title} defers validation and resets it when reopened`, async () => {
    const { browser, page, writes } = await openApp()
    try {
      await page.goto(`${appUrl}${scenario.path}`)
      await page.getByRole('button', { name: scenario.open, exact: true }).click()
      const dialog = page.getByRole('dialog', { name: scenario.title, exact: true })
      await dialog.waitFor()
      assert.equal(await dialog.getByRole('alert').count(), 0)
      assert.equal(await dialog.locator('[aria-invalid="true"]').count(), 0)
      await dialog.getByRole('button', { name: scenario.submit, exact: true }).click()
      for (const error of scenario.errors) await dialog.getByText(error, { exact: true }).waitFor()
      assert.equal(writes.length, 0)
      for (const [label, value] of scenario.fields) await dialog.getByLabel(label, { exact: true }).fill(value)
      assert.equal(await dialog.getByRole('alert').count(), 0)
      await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
      await page.getByRole('button', { name: scenario.open, exact: true }).click()
      for (const [label] of scenario.fields) await dialog.getByLabel(label, { exact: true }).fill('')
      assert.equal(await dialog.getByRole('alert').count(), 0)
      assert.equal(await dialog.locator('[aria-invalid="true"]').count(), 0)
    } finally { await browser.close() }
  })
}

test('reopening a create dialog preserves an in-flight submission guard', async () => {
  const { browser, page } = await openApp()
  let releaseResponse
  const responseReady = new Promise(resolve => { releaseResponse = resolve })
  let requests = 0
  try {
    await page.route('**/v1/customers', async route => {
      if (route.request().method() !== 'POST') return route.fallback()
      requests += 1
      await responseReady
      await route.fulfill({ status: 400, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Customer already exists.' }) })
    })
    await page.goto(`${appUrl}/customers`)
    await page.getByRole('button', { name: 'New customer', exact: true }).click()
    let dialog = page.getByRole('dialog', { name: 'Create customer', exact: true })
    await dialog.getByLabel('Customer id', { exact: true }).fill('customer-1')
    await dialog.getByLabel('Customer name', { exact: true }).fill('Customer One')
    const requestStarted = page.waitForRequest(request => request.method() === 'POST' && new URL(request.url()).pathname === '/v1/customers')
    await dialog.getByRole('button', { name: 'Create customer', exact: true }).click()
    await requestStarted
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await page.getByRole('button', { name: 'New customer', exact: true }).click()
    dialog = page.getByRole('dialog', { name: 'Create customer', exact: true })
    assert.equal(await dialog.getByRole('button', { name: 'Creating…', exact: true }).isDisabled(), true)
    assert.equal(requests, 1)
    releaseResponse()
    await dialog.getByRole('button', { name: 'Create customer', exact: true }).waitFor()
    assert.equal(await dialog.getByRole('button', { name: 'Create customer', exact: true }).isEnabled(), true)
    assert.equal(await dialog.getByRole('alert').count(), 1)
  } finally {
    releaseResponse()
    await browser.close()
  }
})

test('schema drafts defer validation and reset feedback after successful creation', async () => {
  const { browser, page, writes } = await openApp()
  try {
    await page.goto(`${appUrl}/event-definitions/orders.created`)
    const submit = page.getByRole('button', { name: 'Create schema draft', exact: true })
    await submit.waitFor()
    assert.equal(await page.locator('[aria-invalid="true"]').count(), 0)
    assert.equal(await page.getByText('Version is required.', { exact: true }).count(), 0)
    await submit.click()
    await page.getByText('Version is required.', { exact: true }).waitFor()
    await page.getByText('Schema JSON is required.', { exact: true }).waitFor()
    assert.equal(writes.length, 0)
    await page.getByPlaceholder('Version (number)', { exact: true }).fill('1')
    await page.getByPlaceholder('JSON schema', { exact: true }).fill('{"type":"object"}')
    assert.equal(await page.locator('[aria-invalid="true"]').count(), 0)
    await submit.click()
    await page.getByText('Schema draft created.', { exact: true }).waitFor()
    assert.equal(await page.getByPlaceholder('Version (number)', { exact: true }).inputValue(), '')
    assert.equal(await page.getByText('Version is required.', { exact: true }).count(), 0)
    assert.equal(await page.getByText('Schema JSON is required.', { exact: true }).count(), 0)
    assert.equal(writes.length, 1)
  } finally { await browser.close() }
})

test('event replay validates only after the replay action', async () => {
  const { browser, page, writes } = await openApp()
  try {
    await page.goto(`${appUrl}/events/event-1`)
    const input = page.getByPlaceholder('Replay key', { exact: true })
    await input.waitFor()
    assert.equal(await input.getAttribute('aria-invalid'), 'false')
    assert.equal(await page.getByText('Replay key is required.', { exact: true }).count(), 0)
    await page.getByRole('button', { name: 'Replay event', exact: true }).click()
    await page.getByText('Replay key is required.', { exact: true }).waitFor()
    assert.equal(writes.length, 0)
    assert.equal(await page.getByRole('alertdialog').count(), 0)
    await input.fill('replay-1')
    assert.equal(await input.getAttribute('aria-invalid'), 'false')
    assert.equal(await page.getByText('Replay key is required.', { exact: true }).count(), 0)
  } finally { await browser.close() }
})

test('user roles validate on Save even when the loaded user has no roles', async () => {
  const { browser, page, writes } = await openApp()
  try {
    await page.goto(`${appUrl}/users/empty-roles`)
    const save = page.getByRole('button', { name: 'Save roles', exact: true })
    await page.getByRole('checkbox', { name: 'viewer', exact: true }).waitFor()
    assert.equal(await page.getByText('At least one role is required.', { exact: true }).count(), 0)
    await save.click()
    await page.getByText('At least one role is required.', { exact: true }).waitFor()
    assert.equal(writes.length, 0)
    await page.getByRole('checkbox', { name: 'viewer', exact: true }).check()
    assert.equal(await page.getByText('At least one role is required.', { exact: true }).count(), 0)
  } finally { await browser.close() }
})
