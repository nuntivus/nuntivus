import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const contractId = '7521772e-cc7a-4812-8eb2-b508e0b0fb33'
const actor = 'test.admin.with.a.long.identity@example.com'
const operationId = '8f3d6e6c-8dd6-4abc-9f48-8a9522537273'
const rows = [
  { id: 'audit-slo', category: 'contract', action: 'slo_configured', actorId: actor,
    customerId: 'customer-1', contractId, deliveryId: null, eventId: null, operationId,
    details: JSON.stringify({ Request: { LatencyTarget: 99, SuccessTarget: 99.9, SuccessDeadlineSeconds: 86400, LatencyThresholdSeconds: 60 } }),
    createdAt: '2026-11-15T07:28:51Z', expiresAt: '2027-11-15T07:28:51Z' },
  { id: 'audit-delivery', category: 'delivery', action: 'requeue', actorId: 'test.admin',
    customerId: null, contractId: null, deliveryId: 'delivery-1', eventId: 'event-1', operationId,
    details: JSON.stringify({ request: JSON.stringify({ deliveryId: 'delivery-1', reason: 'Retry after recovery', operationId }),
      response: JSON.stringify({ status: 'Pending', attemptCount: 0 }) }),
    createdAt: '2026-11-15T07:20:51Z', expiresAt: '2027-11-15T07:20:51Z' },
  { id: 'audit-invalid', category: 'export', action: 'started', actorId: null,
    customerId: null, contractId: null, deliveryId: null, eventId: null, operationId: null,
    details: '{malformed audit data', createdAt: '2026-11-15T07:10:51Z', expiresAt: '2027-11-15T07:10:51Z' },
]

async function openApp() {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const path = new URL(route.request().url()).pathname
    let body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
    if (path === '/v1/admin/users/me') {
      body = { subject: 'test-admin', roles: ['Admin'], permissions: ['audit-logs.read', 'customers.read', 'contracts.read', 'deliveries.read', 'events.read'] }
    } else if (path === '/v1/admin/audit-logs') {
      body = { ...body, items: rows, totalCount: rows.length, totalPages: 1, hasNext: false }
    } else if (path === '/v1/customers/customer-1') {
      body = { id: 'customer-1', name: 'Customer with an exceptionally long descriptive name' }
    } else if (path === `/v1/contracts/${contractId}`) {
      body = { id: contractId, name: 'Playwright QA Contract 0926 with an exceptionally long descriptive name' }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.goto(`${appUrl}/audit-logs`)
  await page.getByRole('button', { name: 'Preview audit data', exact: true }).nth(2).waitFor()
  return { browser, page }
}

test('audit rows stay compact and single-line with labeled clipped data previews', async () => {
  const { browser, page } = await openApp()
  try {
    const table = page.locator('.audit-table').getByRole('table')
    const firstRow = table.getByRole('row').filter({ hasText: 'Slo Configured' })
    await firstRow.getByRole('link', { name: /Playwright QA Contract/ }).waitFor()
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 900 })
      const measurements = await firstRow.evaluate(row => ({
        height: row.getBoundingClientRect().height,
        wraps: [...row.querySelectorAll('td')].some(cell => getComputedStyle(cell).whiteSpace !== 'nowrap'),
        contractTruncated: row.querySelectorAll('.audit-cell-text')[4].scrollWidth > row.querySelectorAll('.audit-cell-text')[4].clientWidth,
      }))
      assert.ok(measurements.height <= 44, `Audit row must remain compact at ${width}px: ${measurements.height}`)
      assert.equal(measurements.wraps, false)
      assert.equal(measurements.contractTruncated, true)
    }
    const preview = firstRow.getByRole('button', { name: 'Preview audit data', exact: true })
    assert.match(await preview.innerText(), /Latency Target: 99/)
    assert.match(await preview.innerText(), /Success Target: 99.9/)
    assert.doesNotMatch(await preview.innerText(), /[{}]/)
  } finally { await browser.close() }
})

test('audit preview opens a full metadata modal and returns focus on Escape or Close', async () => {
  const { browser, page } = await openApp()
  try {
    const firstRow = page.locator('.audit-table').getByRole('row').filter({ hasText: 'Slo Configured' })
    const preview = firstRow.getByRole('button', { name: 'Preview audit data', exact: true })
    await preview.click()
    const dialog = page.getByRole('dialog', { name: 'Audit log details', exact: true })
    await dialog.waitFor()
    assert.equal(await dialog.getByText(actor, { exact: true }).count(), 1)
    assert.equal(await dialog.getByText(operationId, { exact: true }).count(), 1)
    assert.equal(await dialog.getByText('audit-slo', { exact: true }).count(), 1)
    assert.equal(await page.locator('.audit-table').getByText('Audit payload', { exact: true }).count(), 0)
    assert.deepEqual(JSON.parse(await dialog.locator('pre').innerText()), JSON.parse(rows[0].details))
    assert.equal(await dialog.getByRole('button', { name: 'Copy details', exact: true }).isVisible(), true)
    assert.equal(await dialog.getByPlaceholder('Search audit payload', { exact: true }).isVisible(), true)
    await page.setViewportSize({ width: 390, height: 844 })
    const bounds = await dialog.boundingBox()
    assert.ok(bounds && bounds.width <= 390 && bounds.x >= 0)
    await page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'hidden' })
    assert.equal(await preview.evaluate(button => document.activeElement === button), true)
    assert.equal(await page.getByText('Audit payload', { exact: true }).count(), 0)
    await page.setViewportSize({ width: 1440, height: 900 })
    const eye = firstRow.getByRole('button', { name: 'View audit payload', exact: true })
    await eye.click()
    await dialog.waitFor()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await dialog.waitFor({ state: 'hidden' })
    assert.equal(await eye.evaluate(button => document.activeElement === button), true)
    assert.equal(new URL(page.url()).pathname, '/audit-logs')
  } finally { await browser.close() }
})

test('audit modal expands encoded JSON snapshots and preserves malformed payloads', async () => {
  const { browser, page } = await openApp()
  try {
    const buttons = page.getByRole('button', { name: 'Preview audit data', exact: true })
    await buttons.nth(1).click()
    const dialog = page.getByRole('dialog', { name: 'Audit log details', exact: true })
    await dialog.waitFor()
    assert.deepEqual(JSON.parse(await dialog.locator('pre').innerText()), {
      request: { deliveryId: 'delivery-1', reason: 'Retry after recovery', operationId },
      response: { status: 'Pending', attemptCount: 0 },
    })
    await dialog.getByRole('button', { name: 'Close dialog', exact: true }).click()
    await dialog.waitFor({ state: 'hidden' })
    await buttons.nth(2).click()
    await dialog.waitFor()
    assert.deepEqual(JSON.parse(await dialog.locator('pre').innerText()), { raw: '{malformed audit data' })
  } finally { await browser.close() }
})
