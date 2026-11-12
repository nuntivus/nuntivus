import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
test('portal local login uses a separate session and exposes only read-only customer data', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    const requests = []
    await page.route('https://localhost:7123/**', async route => {
      const request = route.request()
      const path = new URL(request.url()).pathname
      requests.push({ path, method: request.method(), authorization: request.headers().authorization })
      let body = { items: [], page: 1, totalCount: 0, hasNext: false }
      if (path === '/v1/portal/configuration') body = { localEnabled: true, clerkEnabled: false }
      else if (path === '/v1/portal/local/login') body = { accessToken: 'portal-only-token' }
      else if (path === '/v1/portal/customers') body = { ...body, items: [{ id: 'customer-a', name: 'Customer A' }] }
      else if (path.endsWith('/contracts')) body = { ...body, items: [{ id: 'contract-a', enabled: true, eventTypes: ['orders.created'] }] }
      else if (path.endsWith('/deliveries')) body = { ...body, items: [{ id: 'delivery-a', contractId: 'contract-a', eventType: 'orders.created', status: 'Failed', attemptCount: 2 }] }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/portal`)
    await page.getByLabel('Username', { exact: true }).fill('alice')
    await page.getByLabel('Password', { exact: true }).fill('local test password')
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await page.getByRole('cell', { name: 'delivery-a', exact: true }).waitFor()
    assert.equal(await page.getByRole('button', { name: /retry|requeue|cancel|edit/i }).count(), 0)
    assert.equal(await page.evaluate(() => sessionStorage.getItem('nuntivus.dashboard.auth.token')), null)
    assert.equal(await page.evaluate(() => sessionStorage.getItem('nuntivus.portal.token')), 'portal-only-token')
    assert.ok(requests.filter(request => request.method === 'GET' && request.path !== '/v1/portal/configuration').every(request => request.authorization === 'Bearer portal-only-token'))
    assert.ok(requests.every(request => request.path.startsWith('/v1/portal/')))
    await page.getByRole('button', { name: 'Sign out', exact: true }).click()
    await page.getByRole('button', { name: 'Sign in', exact: true }).waitFor()
    assert.equal(await page.getByRole('cell', { name: 'delivery-a', exact: true }).count(), 0)
  } finally { await browser.close() }
})
