import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

async function openSchema(role) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  let activated = false
  const requests = []
  const schema = version => ({ eventType: 'orders.created', version, schema: '{}', compatibilityMode: 'Backward',
    state: version === (activated ? 2 : 1) ? 'Active' : 'Draft', createdAt: '2026-01-01T00:00:00Z', activatedAt: null, deprecatedAt: null })
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const path = new URL(route.request().url()).pathname
    let body = {}
    if (path === '/v1/admin/users/me') {
      body = { subject: 'schema-user', roles: [role], permissions: ['event-definitions.read', 'event-definitions.write'] }
    } else if (path === '/v1/event-definitions/orders.created') {
      body = { eventType: 'orders.created', displayName: 'Orders', description: null, createdAt: '2026-01-01T00:00:00Z' }
    } else if (path === '/v1/event-definitions/orders.created/schemas') {
      body = { items: [schema(1), schema(2)], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 }
    } else if (path === '/v1/event-definitions/orders.created/schemas/2') {
      body = schema(2)
    } else if (path === '/v1/event-definitions/orders.created/schemas/2/compatibility') {
      body = { eventType: 'orders.created', version: 2, againstVersion: activated ? 2 : 1, mode: 'Backward',
        status: activated ? 'Compatible' : 'Breaking', findings: activated ? [] : [
          { path: '/properties/id/required', keyword: 'required', status: 'Breaking', message: 'The receiving schema adds a required property.' },
        ] }
    } else if (path === '/v1/event-definitions/orders.created/schemas/2/activate') {
      requests.push(route.request().postDataJSON())
      activated = true
      body = schema(2)
    } else if (path === '/v1/event-definitions/orders.created/schemas/2/impact') {
      body = { eventType: 'orders.created', page: 1, pageSize: 20, totalContracts: 1,
        evidenceNotice: 'Sample results are evidence, not proof of compatibility.', contracts: [{
          contractId: 'affected-contract', customerId: 'customer-1', name: 'Affected orders', enabled: true,
          status: 'ReviewRequired', reason: 'Transformation requires review.', bindings: [
            { id: 'binding-1', destinationId: null, destinationType: 'Http', deliveryEnabled: true },
          ], samples: [],
        }] }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.goto(`${appUrl}/event-definitions/orders.created`)
  await page.getByRole('button', { name: 'View schema', exact: true }).nth(1).click()
  await page.getByText('Breaking', { exact: true }).waitFor()
  return { browser, page, requests }
}

test('administrator reviews compatibility and confirms an override with a required reason', async () => {
  const { browser, page, requests } = await openSchema('Admin')
  try {
    await page.getByRole('button', { name: 'Override and activate', exact: true }).click()
    await page.getByText('Enter an override reason of at most 2000 characters.', { exact: true }).waitFor()
    assert.equal(requests.length, 0)
    await page.getByLabel('Administrator override reason').fill('Reviewed customer impact')
    await page.getByRole('button', { name: 'Override and activate', exact: true }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Override and activate', exact: true }).click()
    await page.getByText('Compatible', { exact: true }).waitFor()
    assert.deepEqual(requests, [{ overrideCompatibility: true, reason: 'Reviewed customer impact' }])
  } finally { await browser.close() }
})

test('schema impact is explicitly requested and labels sample evidence conservatively', async () => {
  const { browser, page, requests } = await openSchema('Operator')
  try {
    assert.equal(await page.getByText('Affected orders', { exact: true }).count(), 0)
    await page.getByRole('button', { name: 'Analyze affected contracts', exact: true }).click()
    const row = page.getByRole('row', { name: 'Open contract Affected orders', exact: true })
    await row.waitFor()
    assert.equal(await row.locator('.table-detail-indicator').count(), 1)
    assert.equal(await row.getByRole('link', { name: 'Affected orders', exact: true }).count(), 0)
    await page.getByText('Sample results are evidence, not proof of compatibility.', { exact: true }).waitFor()
    await page.getByText('Review required', { exact: true }).waitFor()
    assert.equal(requests.length, 0)
  } finally { await browser.close() }
})

test('operator sees compatibility findings but cannot request an override', async () => {
  const { browser, page } = await openSchema('Operator')
  try {
    assert.equal(await page.getByLabel('Administrator override reason').count(), 0)
    assert.equal(await page.getByRole('button', { name: 'Override and activate', exact: true }).count(), 0)
    await page.getByText('Activation is blocked.', { exact: false }).waitFor()
  } finally { await browser.close() }
})
