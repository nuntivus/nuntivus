import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const fullError = `HTTP 405: ${'Response body with detailed explanation. '.repeat(30)}`

async function openPage() {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  await page.addInitScript(() => {
    window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
  })
  await page.route('https://localhost:7123/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let body = {}
    if (path === '/v1/admin/users/me') {
      body = { subject: 'admin', roles: ['Admin'], permissions: [
        'deliveries.read', 'delivery-failures.read', 'events.read', 'customers.read', 'contracts.read', 'sources.read',
      ] }
    } else if (path === '/v1/deliveries') {
      body = { items: [{
        id: 'delivery-1', eventId: 'event-1', customerId: 'customer-1', contractId: 'contract-1',
        eventType: 'order.created', status: 3, attemptCount: 1, lastError: fullError,
        latestFailure: { failureCategory: 'Permanent', error: fullError, failedAt: '2026-01-01T00:01:00Z' },
        updatedAt: '2026-01-01T00:01:00Z',
      }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }
    } else if (path === '/v1/deliveries/delivery-1') {
      body = {
        id: 'delivery-1', eventId: 'event-1', customerId: 'customer-1', contractId: 'contract-1',
        eventType: 'order.created', destinationType: 'Http', status: 3, attemptCount: 1,
        lastError: fullError, nextAttemptAt: null,
        createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:01:00Z',
      }
    } else if (path === '/v1/deliveries/delivery-1/attempts') {
      body = [{
        id: 'attempt-1', attemptNumber: 1, succeeded: false, failureCategory: 'Permanent', error: fullError,
        metadata: { protocol: 'http', httpStatus: '405' }, startedAt: '2026-01-01T00:00:00Z',
        completedAt: '2026-01-01T00:00:01Z', durationMs: 1000,
      }]
    } else if (path === '/v1/customers/customer-1') {
      body = { id: 'customer-1', name: 'Acme Customer' }
    } else if (path === '/v1/contracts/contract-1') {
      body = { id: 'contract-1', name: 'Order notifications' }
    } else if (path === '/v1/sources/source-1') {
      body = { id: 'source-1', name: 'Order API' }
    } else if (path === '/v1/events/event-1') {
      body = { id: 'event-1', type: 'order.created', source: 'source-1', deliveries: [], payload: {}, metadata: {} }
    } else if (path === '/v1/events') {
      body = { items: [{
        id: 'event-1', type: 'order.created', source: 'source-1', deliveries: [],
        occurredAt: '2026-01-01T00:00:00Z', receivedAt: '2026-01-01T00:00:01Z',
      }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page }
}

test('failed deliveries show status labels, related names, and full error on demand', async () => {
  const { browser, page } = await openPage()
  try {
    await page.goto(`${appUrl}/deliveries/failures`)
    const table = page.getByRole('table')
    await table.getByText('Failed', { exact: true }).waitFor()
    assert.equal(await table.getByText('3', { exact: true }).count(), 0)
    assert.equal(await table.getByText('Acme Customer').count(), 1)
    assert.equal(await table.getByText('Order notifications').count(), 1)
    const preview = table.getByRole('button', { name: 'Open error details' })
    assert.equal(await preview.locator('span').evaluate((element) => element.scrollWidth > element.clientWidth), true)
    await preview.click()
    assert.equal(await page.getByRole('dialog').locator('pre').textContent(), fullError)
  } finally {
    await browser.close()
  }
})

test('delivery attempt error opens complete response and related event uses its type', async () => {
  const { browser, page } = await openPage()
  try {
    await page.goto(`${appUrl}/deliveries/delivery-1`)
    const attempts = page.getByText('Delivery attempts', { exact: true }).locator('..').locator('..')
    await attempts.getByRole('button', { name: 'Open attempt 1 error' }).click()
    assert.equal(await page.getByRole('dialog').locator('pre').textContent(), fullError)
    await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click()
    assert.equal(await page.locator('a[href="/events/event-1"]').count() > 0, true)
  } finally {
    await browser.close()
  }
})

test('events show source name instead of stored source ID', async () => {
  const { browser, page } = await openPage()
  try {
    await page.goto(`${appUrl}/events`)
    await page.getByRole('link', { name: 'Order API' }).waitFor()
    assert.equal(await page.getByText('source-1', { exact: true }).count(), 0)
  } finally {
    await browser.close()
  }
})
