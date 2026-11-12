import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

for (const scenario of [
  { status: 'Valid', version: 3, label: 'Valid', versionLabel: 'Version 3' },
  { status: 'NotConfigured', version: null, label: 'No active schema at ingestion', versionLabel: 'None recorded' },
  { status: 'Unknown', version: null, label: 'Not recorded', versionLabel: 'None recorded' },
]) {
  test(`event detail displays ${scenario.status} schema validation`, async () => {
    const browser = await chromium.launch({ headless: true })
    try {
      const page = await browser.newPage()
      await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const path = new URL(route.request().url()).pathname
        let body = {}
        if (path === '/v1/admin/users/me') {
          body = { subject: 'test-admin', roles: ['Admin'], permissions: ['events.read', 'sources.read'] }
        } else if (path === '/v1/events/event-schema') {
          body = {
            id: 'event-schema', idempotencyKey: 'key', type: 'orders.created', source: 'source-1',
            occurredAt: '2026-01-01T00:00:00Z', receivedAt: '2026-01-01T00:00:01Z',
            payload: { orderId: 42 }, metadata: {}, deliveries: [],
            inputSchemaVersion: scenario.version, schemaValidationStatus: scenario.status,
          }
        } else if (path === '/v1/sources/source-1') {
          body = { id: 'source-1', name: 'Orders API' }
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/events/event-schema`)
      const information = page.locator('.event-information-list')
      await information.getByText(scenario.label, { exact: true }).waitFor()
      assert.equal(await information.getByText(scenario.versionLabel, { exact: true }).count(), 1)
    } finally {
      await browser.close()
    }
  })
}
