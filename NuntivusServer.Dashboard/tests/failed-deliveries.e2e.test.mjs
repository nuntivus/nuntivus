import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const statusNames = ['Pending', 'InFlight', 'Delivered', 'Failed', 'Dead']

async function openApp(states) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage({ acceptDownloads: true })
  const requests = []
  const exports = []
  const rows = states.map((status, index) => ({
    id: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
    eventId: '63f6cdd6-79cc-4c08-9892-e2c70c76c356',
    contractId: '7521772e-cc7a-4812-8eb2-b508e0b0fb33', customerId: 'customer-1',
    eventType: 'orders.created', destinationId: null, destinationType: 'Http',
    status: statusNames.indexOf(status), attemptCount: 1, lastError: 'HTTP 500', nextAttemptAt: null,
    createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
    latestFailure: { failureCategory: 'Transient', error: 'HTTP 500', failedAt: '2026-01-01T00:00:00Z' },
    attemptSummary: { totalAttempts: 1, successfulAttempts: 0, failedAttempts: 1, lastAttemptAt: '2026-01-01T00:00:00Z' },
  }))
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const url = new URL(route.request().url())
    let body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNext: false }
    if (url.pathname === '/v1/admin/users/me') {
      body = { subject: 'test-admin', roles: ['Operator'], permissions: ['delivery-failures.read', 'deliveries.read', 'customers.read', 'contracts.read'] }
    } else if (url.pathname === '/v1/deliveries') {
      requests.push(url)
      let items = rows
      if (url.searchParams.get('failuresOnly') === 'true') items = items.filter(row => ['Failed', 'Dead'].includes(statusNames[row.status]))
      if (url.searchParams.has('status')) items = items.filter(row => statusNames[row.status] === url.searchParams.get('status'))
      body = { ...body, items, totalCount: items.length, totalPages: items.length ? 1 : 0 }
    } else if (url.pathname === '/v1/exports/deliveries') {
      exports.push(url)
      await route.fulfill({ status: 200, contentType: 'text/csv', body: '"id"\r\n' })
      return
    } else if (url.pathname === '/v1/customers/customer-1') {
      body = { id: 'customer-1', name: 'Customer One' }
    } else if (url.pathname.startsWith('/v1/contracts/')) {
      body = { id: '7521772e-cc7a-4812-8eb2-b508e0b0fb33', name: 'Orders contract' }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page, requests, exports }
}

test('failure list defaults to both states, supports narrowing and exports the combined filter', async () => {
  const { browser, page, requests, exports } = await openApp(['Failed', 'Dead', 'Delivered', 'Pending'])
  try {
    await page.goto(`${appUrl}/deliveries/failures`)
    const table = page.locator('[aria-label="Failed deliveries table"]').getByRole('table')
    await table.getByText('Dead', { exact: true }).waitFor()
    assert.equal(await table.locator('tbody tr').evaluateAll(rows => rows.every(row => row.getBoundingClientRect().height <= 48)), true)
    assert.equal(await table.getByText('No automatic retry', { exact: true }).count(), 0)
    assert.equal(await table.locator('.failure-cell-text').evaluateAll(cells => cells.every(cell => getComputedStyle(cell).whiteSpace === 'nowrap')), true)
    assert.equal(await table.getByText('Failed', { exact: true }).count(), 1)
    assert.equal(await table.getByText('Delivered', { exact: true }).count(), 0)
    assert.equal(await table.getByText('Pending', { exact: true }).count(), 0)
    assert.equal(await page.getByRole('combobox', { name: 'Failure status' }).innerText(), 'Failed & dead')
    assert.equal(requests.at(-1).searchParams.get('failuresOnly'), 'true')
    assert.equal(requests.at(-1).searchParams.has('status'), false)

    for (const status of ['Failed', 'Dead']) {
      await page.getByRole('combobox', { name: 'Failure status' }).click()
      const response = page.waitForResponse(response => {
        const url = new URL(response.url())
        return url.pathname === '/v1/deliveries' && url.searchParams.get('status') === status
          && url.searchParams.get('failuresOnly') === 'true' && response.ok()
      })
      await page.getByRole('option', { name: `${status} only`, exact: true }).click()
      await page.waitForURL(url => url.searchParams.get('status') === status)
      await response
      await page.waitForFunction(expected => {
        const rows = document.querySelectorAll('[aria-label="Failed deliveries table"] table tbody tr')
        return rows.length === 1 && rows[0].querySelector('.rt-Badge')?.textContent === expected
      }, status)
      const rowId = `00000000-0000-4000-8000-${status === 'Failed' ? '000000000001' : '000000000002'}`
      const row = table.getByRole('row', { name: `Open delivery ${rowId}`, exact: true })
      await row.getByText(status, { exact: true }).waitFor()
      await table.getByText(status === 'Failed' ? 'Dead' : 'Failed', { exact: true }).waitFor({ state: 'hidden' })
      assert.equal(await row.getByText(status, { exact: true }).count(), 1)
      assert.equal(requests.at(-1).searchParams.get('failuresOnly'), 'true')
    }

    await page.getByRole('button', { name: 'Reset', exact: true }).click()
    await page.waitForURL(url => !url.searchParams.has('status'))
    await table.getByText('Failed', { exact: true }).waitFor()
    await table.getByText('Dead', { exact: true }).waitFor()
    assert.equal(await table.getByText('Dead', { exact: true }).count(), 1)
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Export CSV', exact: true }).click()
    await download
    assert.equal(exports.length, 1)
    assert.equal(exports[0].searchParams.get('failuresOnly'), 'true')
    assert.equal(exports[0].searchParams.has('status'), false)
  } finally { await browser.close() }
})

test('dead deliveries remain visible by default when there are no Failed rows', async () => {
  const { browser, page, requests } = await openApp(['Dead', 'Delivered'])
  try {
    await page.goto(`${appUrl}/deliveries/failures`)
    const table = page.locator('[aria-label="Failed deliveries table"]').getByRole('table')
    await table.getByText('Dead', { exact: true }).waitFor()
    assert.equal(await table.getByText('Delivered', { exact: true }).count(), 0)
    await page.getByRole('combobox', { name: 'Failure status' }).click()
    await page.getByRole('option', { name: 'Failed only', exact: true }).click()
    await page.getByText('No failed/dead deliveries found for current filters.', { exact: true }).waitFor()
    await page.getByRole('button', { name: 'Reset', exact: true }).click()
    await table.getByText('Dead', { exact: true }).waitFor()
    assert.equal(new URL(page.url()).searchParams.has('status'), false)
    assert.ok(requests.some(url => url.searchParams.get('failuresOnly') === 'true' && !url.searchParams.has('status')))
  } finally { await browser.close() }
})
