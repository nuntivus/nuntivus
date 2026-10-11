import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

const contract = {
  id: 'contract-one', name: 'Orders route', customerId: 'acme', enabled: true,
  sourceIds: ['source-one'], eventTypes: ['orders.created'], destinations: [
    { id: 'binding-one', destinationId: 'destination-one', type: 'Http', deliveryEnabled: true },
    { id: 'binding-two', destinationId: null, type: 'Kafka', deliveryEnabled: false },
  ],
  filter: { payloadRules: [{ selector: '$.id', operator: 'Equals', value: '1' }] },
}

const pageResult = (items) => ({ items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1, hasNext: false, hasPrevious: false })

test('routing map shows configuration, detached bindings and filters contracts', async () => {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  try {
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async (route) => {
      const path = new URL(route.request().url()).pathname
      const body = path === '/v1/admin/users/me'
        ? { subject: 'admin', roles: ['Admin'], permissions: ['contracts.read'] }
        : path === '/v1/admin/contracts' ? pageResult([contract])
          : path === '/v1/sources' ? pageResult([{ id: 'source-one', name: 'Orders API', type: 'HTTP_API', enabled: true }])
            : path === '/v1/destinations' ? pageResult([{ id: 'destination-one', name: 'Orders webhook', type: 'Http', enabled: true, configured: true }]) : {}
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/routing-map`)
    const graph = page.getByRole('region', { name: 'Configured routing graph' })
    await graph.getByText('Orders route').waitFor()
    assert.equal(await graph.getByText('Orders API').count(), 1)
    assert.equal(await graph.getByText('Orders webhook').count(), 1)
    assert.equal(await graph.getByText('Kafka (detached)').count(), 1)
    assert.equal(await graph.locator('.react-flow__edge').count(), 3)
    assert.equal(await graph.locator('.react-flow__handle').count(), 6)
    const destinationCards = await graph.locator('.react-flow__node[data-id^="destination:"], .react-flow__node[data-id^="detached:"]').all()
    const destinationBounds = (await Promise.all(destinationCards.map((card) => card.boundingBox()))).sort((a, b) => a.y - b.y)
    assert.equal(destinationBounds[0].y + destinationBounds[0].height < destinationBounds[1].y, true, JSON.stringify(destinationBounds))
    await graph.getByText('Orders route').click()
    await page.getByText('Pipeline: Filters').waitFor()
    assert.equal(await graph.locator('.route-edge-highlighted').count(), 3)
    assert.equal(await page.getByLabel('Selected flow path').getByRole('button', { name: 'Orders API' }).count(), 1)
    await graph.getByText('Orders webhook').click()
    assert.equal(await graph.locator('.route-edge-highlighted').count(), 2)
    assert.equal(await graph.locator('.route-edge-muted').count(), 1)
    await page.getByRole('button', { name: 'Clear path' }).click()
    assert.equal(await graph.locator('.route-edge-muted').count(), 0)
    await graph.getByText('Orders route').click()
    assert.equal(await page.getByRole('link', { name: 'Open contract' }).getAttribute('href'), '/contracts/contract-one')
    await page.getByPlaceholder('Name, ID, event type...').fill('no matching route')
    await page.getByText('No contracts match these filters.').waitFor()
  } finally {
    await browser.close()
  }
})

test('shared sources highlight every downstream route without merging their edge ports', async () => {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  try {
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async (route) => {
      const path = new URL(route.request().url()).pathname
      const body = path === '/v1/admin/users/me'
        ? { subject: 'admin', roles: ['Admin'], permissions: ['contracts.read'] }
        : path === '/v1/admin/contracts' ? pageResult([
          { ...contract, destinations: [contract.destinations[0]] },
          { ...contract, id: 'contract-two', name: 'Alerts route', destinations: [{ ...contract.destinations[0], id: 'binding-three' }] },
          { ...contract, id: 'contract-three', name: 'Independent route', sourceIds: ['source-two'], destinations: [{ ...contract.destinations[0], id: 'binding-four', destinationId: 'destination-two' }] },
        ]) : path === '/v1/sources' ? pageResult([
          { id: 'source-one', name: 'Orders API', type: 'HTTP_API', enabled: true },
          { id: 'source-two', name: 'Other API', type: 'HTTP_API', enabled: true },
        ]) : path === '/v1/destinations' ? pageResult([
          { id: 'destination-one', name: 'Orders webhook', type: 'Http', enabled: true, configured: true },
          { id: 'destination-two', name: 'Other webhook', type: 'Http', enabled: true, configured: true },
        ]) : {}
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/routing-map`)
    const graph = page.getByRole('region', { name: 'Configured routing graph' })
    await graph.getByText('Independent route').waitFor()
    assert.equal(await graph.locator('[data-id="source:source-one"] .react-flow__handle-right').count(), 2)
    assert.equal(await graph.locator('[data-id="destination:destination-one"] .react-flow__handle-left').count(), 2)
    for (const kind of ['source:', 'contract:', 'destination:']) {
      const cards = await graph.locator(`.react-flow__node[data-id^="${kind}"]`).all()
      const bounds = await Promise.all(cards.map((card) => card.boundingBox()))
      const ordered = bounds.sort((a, b) => a.y - b.y)
      for (let index = 1; index < ordered.length; index++) {
        assert.equal(ordered[index - 1].y + ordered[index - 1].height < ordered[index].y, true, `${kind} nodes must not overlap: ${JSON.stringify(ordered)}`)
      }
    }
    await graph.getByText('Orders API').click()
    assert.equal(await graph.locator('.route-edge-highlighted').count(), 4)
    assert.equal(await graph.locator('.route-edge-muted').count(), 2)
    assert.equal(await page.getByLabel('Selected flow path').getByRole('button', { name: 'Alerts route' }).count(), 1)
    assert.equal(await page.getByLabel('Selected flow path').getByRole('button', { name: 'Independent route' }).count(), 0)
    await page.getByLabel('Selected flow path').getByRole('button', { name: 'Orders webhook' }).click()
    assert.equal(await graph.locator('.route-edge-highlighted').count(), 4)
    await page.getByPlaceholder('Name, ID, event type...').fill('Independent route')
    await graph.getByText('Independent route').waitFor()
    assert.equal(await graph.locator('.route-edge-muted').count(), 0)
    assert.equal(await page.getByRole('button', { name: 'Clear path' }).count(), 0)
  } finally {
    await browser.close()
  }
})

test('routing map denies users without contracts.read', async () => {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  try {
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ subject: 'admin', roles: [], permissions: [] }) })
    })
    await page.goto(`${appUrl}/routing-map`)
    assert.equal(await page.getByRole('region', { name: 'Configured routing graph' }).count(), 0)
  } finally {
    await browser.close()
  }
})
