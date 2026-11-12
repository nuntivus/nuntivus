import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('prepared payload preview shows the recorded identity without sending a delivery', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    let sends = 0
    const contract = { id: 'typed-contract', customerId: 'customer-1', name: 'Typed orders', enabled: true,
      eventTypes: ['orders.created'], sourceIds: ['source-1'], destinationType: 'Http', destinationConfiguration: '{}',
      schemaTargets: [], createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      let body = {}
      if (path === '/v1/admin/users/me') body = { subject: 'operator', roles: ['Operator'], permissions: ['contracts.read', 'events.publish'] }
      else if (path === '/v1/contracts/typed-contract') body = contract
      else if (path === '/v1/contracts' || path === '/v1/admin/contracts') body = { items: [contract], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 }
      else if (path === '/v1/contracts/typed-contract/test/preview') body = { matches: true, reason: 'Prepared successfully.', transformedPayload: { orderId: 42, label: null }, transformationIdentity: 'a'.repeat(64), outputSchemaVersion: 3 }
      else if (path === '/v1/contracts/typed-contract/dry-run') body = { wouldDeliver: true, inputSchemaVersion: 1, outputSchemaVersion: 3,
        transformationIdentity: 'a'.repeat(64), transformedPayload: { orderId: 42 },
        stages: [{ stage: 'inputSchema', status: 'Passed', message: 'Input is valid.' }, { stage: 'runtimeControls', status: 'NotSimulated', message: 'No reservations made.' }],
        destinations: [{ bindingId: 'binding-1', destinationId: null, type: 'Http', enabled: true, status: 'NotConnected', message: 'No network access.' }],
        notice: 'Nothing is saved or sent by this simulation.' }
      else if (path.endsWith('/test/send')) { sends += 1 }
      else if (path === '/v1/customers/customer-1') body = { id: 'customer-1', name: 'Customer' }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/test-events?contractId=typed-contract`)
    await page.getByRole('button', { name: 'Preview match', exact: true }).click()
    await page.getByText('Prepared output payload', { exact: true }).waitFor()
    assert.match(await page.locator('pre').innerText(), /"orderId": 42/)
    assert.match(await page.locator('pre').innerText(), /"label": null/)
    await page.getByText('Output schema: 3', { exact: false }).waitFor()
    assert.equal(sends, 0)
    assert.equal(await page.getByRole('button', { name: 'Send test event', exact: true }).isEnabled(), true)
    await page.getByRole('button', { name: 'Run without sending', exact: true }).click()
    await page.getByText('Checks passed', { exact: true }).waitFor()
    await page.getByText('Nothing is saved or sent by this simulation.', { exact: true }).waitFor()
    await page.getByText('runtimeControls: NotSimulated', { exact: false }).waitFor()
    assert.equal(sends, 0)
  } finally { await browser.close() }
})
