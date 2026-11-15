import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const timestamp = '2026-01-01T00:00:00Z'
const customer = { id: 'customer-1', name: 'Customer One', enabled: true, createdAt: timestamp, updatedAt: timestamp }
const source = { id: 'source-1', name: 'Orders source', type: 'HTTP_API', enabled: true, description: null, configuration: { authentication: { type: 'none' }, method: 'POST' }, createdAt: timestamp, updatedAt: timestamp }
const destination = { id: 'destination-1', name: 'Orders destination', type: 'Http', configured: true, enabled: true, description: null, configuration: JSON.stringify({ url: 'https://example.com/hook', authentication: { type: 'none' } }), createdAt: timestamp, updatedAt: timestamp }
const contract = { id: 'contract-1', name: 'Orders contract', customerId: customer.id, eventTypes: ['orders.created'], sourceIds: [source.id], destinationType: 'Http', destinationConfiguration: destination.configuration, destinations: [], enabled: true, createdAt: timestamp, updatedAt: timestamp }
const delivery = { id: 'delivery-1', eventId: 'event-1', customerId: customer.id, contractId: contract.id, eventType: 'orders.created', destinationType: 'Http', status: 'Failed', attemptCount: 1, lastError: 'Timeout', nextAttemptAt: null, createdAt: timestamp, updatedAt: timestamp }
const failure = { ...delivery, deliveryId: delivery.id }
const paged = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1, hasNext: false })

async function openApp(listContract = contract) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const path = new URL(route.request().url()).pathname
    let body = paged([])
    if (path === '/v1/admin/users/me') body = { subject: 'navigation-user', roles: ['Admin'], permissions: ['contracts.read', 'contracts.write', 'customers.read', 'deliveries.read', 'delivery-failures.read', 'audit-logs.read', 'event-definitions.read'] }
    else if (path === '/v1/sources') body = paged([source])
    else if (path === `/v1/sources/${source.id}`) body = source
    else if (path === '/v1/destinations') body = paged([destination])
    else if (path === `/v1/destinations/${destination.id}`) body = destination
    else if (path === '/v1/admin/contracts' || path === `/v1/customers/${customer.id}/contracts`) body = paged([listContract])
    else if (path === `/v1/contracts/${contract.id}`) body = contract
    else if (path === '/v1/customers') body = paged([customer])
    else if (path === `/v1/customers/${customer.id}`) body = customer
    else if (path.startsWith('/v1/destination-health/')) body = []
    else if (path === '/v1/deliveries') body = paged([delivery])
    else if (path === `/v1/deliveries/${delivery.id}`) body = delivery
    else if (path === `/v1/deliveries/${delivery.id}/attempts`) body = []
    else if (path === '/v1/admin/deliveries/failures') body = paged([failure])
    else if (path.startsWith('/v1/event-definitions/')) {
      const eventType = decodeURIComponent(path.split('/')[3])
      body = path.endsWith('/schemas') ? paged([])
        : path.endsWith('/contracts') ? []
        : { eventType, displayName: eventType, description: null, createdAt: timestamp }
    }
    else if (path === '/v1/admin/audit-logs') body = paged([
      { id: 'audit-1', category: 'Delivery', action: 'retry', actorId: 'operator', deliveryId: delivery.id, details: '{}', createdAt: timestamp },
      { id: 'audit-2', category: 'Security', action: 'login', actorId: 'no-detail-target', deliveryId: null, details: '{}', createdAt: timestamp },
    ])
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page }
}

test('contract event type links open definitions instead of the contract row', async () => {
  const eventTypes = ['orders.created', 'orders/updated']
  const { browser, page } = await openApp({ ...contract, eventTypes })
  try {
    for (const eventType of eventTypes) {
      await page.goto(`${appUrl}/contracts`)
      const row = page.getByRole('row', { name: 'Open contract Orders contract', exact: true })
      const link = row.getByRole('link', { name: eventType, exact: true })
      assert.equal(await link.getAttribute('href'), `/event-definitions/${encodeURIComponent(eventType)}`)
      if (eventType === eventTypes[0]) await link.click()
      else { await link.focus(); await page.keyboard.press('Enter') }
      await page.waitForURL(`${appUrl}/event-definitions/${encodeURIComponent(eventType)}`)
    }
  } finally { await browser.close() }
})

test('contract destination type column displays names for numeric and string API values', async () => {
  for (const [destinationType, label] of [[1, 'HTTP'], [2, 'Kafka'], [3, 'NATS'], [4, 'RabbitMQ'], [5, 'SQS'], ['Http', 'HTTP'], ['Sqs', 'SQS']]) {
    const { browser, page } = await openApp({ ...contract, destinationType })
    try {
      await page.goto(`${appUrl}/contracts`)
      const row = page.getByRole('row', { name: 'Open contract Orders contract', exact: true })
      await row.waitFor()
      assert.equal(await page.getByRole('columnheader', { name: 'Destination type', exact: true }).count(), 1)
      assert.equal(await row.getByRole('cell', { name: label, exact: true }).count(), 1)
    } finally { await browser.close() }
  }
})

for (const [path, label, name, id] of [
  ['sources', 'Source Orders source', source.name, source.id],
  ['destinations', 'Destination Orders destination', destination.name, destination.id],
]) {
  test(`${path} use row navigation with a visual-only detail indicator and keyboard access`, async () => {
    const { browser, page } = await openApp()
    try {
      for (const action of ['cell', 'indicator', 'Enter', 'Space']) {
        await page.goto(`${appUrl}/${path}`)
        const row = page.getByRole('row', { name: `Open ${label}`, exact: true })
        await row.waitFor()
        const indicator = row.locator('.table-detail-indicator')
        assert.equal(await indicator.count(), 1)
        assert.equal(await indicator.getAttribute('aria-hidden'), 'true')
        assert.equal(await indicator.locator('a, button, [tabindex]').count(), 0)
        assert.equal(await row.getByRole('link', { name, exact: true }).count(), 0)
        assert.equal(await row.getByRole('link', { name: 'Details', exact: true }).count(), 0)
        if (action === 'cell') await row.getByRole('cell').first().click()
        else if (action === 'indicator') await indicator.locator('svg').click()
        else { await row.focus(); await page.keyboard.press(action) }
        await page.waitForURL(`${appUrl}/${path}/${id}`)
      }
    } finally { await browser.close() }
  })
}

test('nested SVG actions, checkboxes, and edit links do not trigger row navigation', async () => {
  const { browser, page } = await openApp()
  try {
    await page.goto(`${appUrl}/sources`)
    const sourceRow = page.getByRole('row', { name: 'Open Source Orders source', exact: true })
    await sourceRow.getByRole('button', { name: 'Copy endpoint for Orders source', exact: true }).locator('svg').click()
    await page.getByText('Ingestion endpoint copied.', { exact: true }).waitFor()
    assert.equal(new URL(page.url()).pathname, '/sources')
    await sourceRow.getByRole('link', { name: 'Edit', exact: true }).click()
    await page.waitForURL(`${appUrl}/sources/source-1?edit=1`)

    await page.goto(`${appUrl}/contracts`)
    const contractRow = page.getByRole('row', { name: 'Open contract Orders contract', exact: true })
    const checkbox = contractRow.getByRole('checkbox', { name: 'Select contract contract-1', exact: true })
    await checkbox.check()
    assert.equal(await checkbox.isChecked(), true)
    assert.equal(new URL(page.url()).pathname, '/contracts')
    await contractRow.getByRole('link', { name: 'Send test event', exact: true }).locator('svg').click()
    await page.waitForURL(`${appUrl}/test-events?contractId=contract-1`)
  } finally { await browser.close() }
})

test('customer related-record rows show indicators and navigate without duplicate detail links', async () => {
  const { browser, page } = await openApp()
  try {
    await page.goto(`${appUrl}/customers/customer-1`)
    const contractRow = page.getByRole('row', { name: 'Open contract Orders contract', exact: true })
    await contractRow.waitFor()
    assert.equal(await contractRow.locator('.table-detail-indicator').count(), 1)
    assert.equal(await contractRow.getByRole('link', { name: 'Orders contract', exact: true }).count(), 0)
    const deliveryRows = page.getByRole('row', { name: 'Open delivery delivery-1', exact: true })
    await deliveryRows.nth(1).waitFor()
    assert.equal(await deliveryRows.count(), 2)
    assert.equal(await deliveryRows.locator('.table-detail-indicator').count(), 2)
    await contractRow.getByRole('button', { name: 'Deliveries', exact: true }).click()
    await page.waitForURL(`${appUrl}/deliveries?customerId=customer-1&contractId=contract-1`)
    await page.goto(`${appUrl}/customers/customer-1`)
    await page.getByRole('row', { name: 'Open delivery delivery-1', exact: true }).first().getByRole('rowheader').click()
    await page.waitForURL(`${appUrl}/deliveries/delivery-1`)
  } finally { await browser.close() }
})

test('audit rows navigate only when a delivery target exists and payload actions stay independent', async () => {
  const { browser, page } = await openApp()
  try {
    await page.goto(`${appUrl}/audit-logs`)
    const row = page.getByRole('row', { name: 'Open delivery delivery-1', exact: true })
    await row.waitFor()
    assert.equal(await row.locator('.table-detail-indicator').count(), 1)
    const informational = page.getByRole('row').filter({ hasText: 'no-detail-target' })
    assert.equal(await informational.getAttribute('tabindex'), null)
    assert.equal(await informational.locator('.table-detail-indicator').count(), 0)
    await row.getByRole('button', { name: 'View audit payload', exact: true }).locator('svg').click()
    const dialog = page.getByRole('dialog', { name: 'Audit log details', exact: true })
    await dialog.waitFor()
    assert.equal(new URL(page.url()).pathname, '/audit-logs')
    await dialog.getByRole('button', { name: 'Close dialog', exact: true }).click()
    await dialog.waitFor({ state: 'hidden' })
    await row.getByRole('cell').first().click()
    await page.waitForURL(`${appUrl}/deliveries/delivery-1`)
  } finally { await browser.close() }
})
