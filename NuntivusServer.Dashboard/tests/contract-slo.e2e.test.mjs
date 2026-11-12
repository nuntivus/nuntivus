import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('contract SLOs show partial coverage and never claim no traffic is perfect', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    const writes = []
    const budget = { eligible: 0, good: 0, bad: 0, pending: 2, attainmentPercent: null, allowedFailures: 0, remainingFailures: 0, budgetRemainingPercent: null }
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      let body = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }
      if (path === '/v1/admin/users/me') body = { subject: 'slo-operator', roles: ['Operator'], permissions: ['contracts.read', 'contracts.write'] }
      else if (path === '/v1/destination-health/contracts/slo-contract') body = { contractId: 'slo-contract', customerId: 'customer-1', destinationType: 'Http',
        consecutiveFailures: 0, lastSuccessAt: null, lastFailureAt: null, lastAttemptAt: '2026-01-01T00:00:00Z', lastLatencyMs: null,
        averageLatencyMs: null, latencySampleCount: 0, status: 'Healthy', updatedAt: '2026-01-01T00:00:00Z' }
      else if (path === '/v1/contracts/slo-contract') body = { id: 'slo-contract', customerId: 'customer-1', name: 'SLO orders', enabled: true,
        eventTypes: ['orders.created'], sourceIds: [], schemaTargets: [], destinationType: 'Http', destinationConfiguration: '{}', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }
      else if (path === '/v1/contracts/slo-contract/slo') {
        if (route.request().method() === 'PUT') writes.push(route.request().postDataJSON())
        body = { policy: { successTarget: 99.9, latencyTarget: 99, latencyThresholdSeconds: 60, successDeadlineSeconds: 86400 },
          monitoringStartedAt: '2026-01-01T00:00:00Z', windowStart: '2026-01-01T00:00:00Z', windowEnd: '2026-01-02T00:00:00Z',
          fullWindowCovered: false, success: budget, latency: budget, excluded: 1, notice: 'No eligible traffic is not 100% compliance.' }
      }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/contracts/slo-contract`)
    try { await page.getByRole('heading', { name: 'Contract SLOs · rolling 30 days', exact: true }).waitFor({ timeout: 10000 }) }
    catch (error) { throw new Error(`${error.message}\nPage: ${await page.locator('body').innerText()}\nErrors: ${errors.join('; ')}`) }
    assert.equal(await page.getByText('No eligible traffic', { exact: true }).count(), 2)
    await page.getByText('Partial monitoring window', { exact: false }).waitFor()
    await page.getByRole('button', { name: 'Configure SLOs', exact: true }).click()
    assert.equal(await page.getByLabel('Success deadline (seconds)', { exact: true }).inputValue(), '86400')
    await page.getByLabel('Success target (%)', { exact: true }).fill('101')
    await page.getByRole('button', { name: 'Save policy', exact: true }).click()
    await page.getByText('Use targets above 0 up to 100', { exact: false }).waitFor()
    assert.equal(writes.length, 0)
    await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  } finally { await browser.close() }
})
