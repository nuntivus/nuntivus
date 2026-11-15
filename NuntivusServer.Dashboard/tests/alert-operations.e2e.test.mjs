import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
for (const role of ['Operator', 'Viewer']) {
  test(`${role} can inspect webhook history with role-gated retry and archive`, async () => {
    const browser = await chromium.launch({ headless: true })
    try {
      const page = await browser.newPage()
      let archived = false
      let retried = false
      let operationId
      await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const path = new URL(route.request().url()).pathname
        let body = { items: [], page: 1, pageSize: 50, totalCount: 0, hasNext: false }
        if (path === '/v1/admin/users/me') body = { subject: 'operations', roles: [role], permissions: ['contracts.read', ...(role === 'Operator' ? ['contracts.write'] : [])] }
        else if (route.request().method() === 'DELETE') { archived = true; body = {} }
        else if (path.endsWith('/webhook/retry')) { operationId = route.request().postDataJSON().operationId; retried = true; body = {} }
        else if (path.endsWith('/webhook/attempts')) body = { ...body, items: [{ attemptNumber: 10, startedAt: '2026-01-01T00:00:00Z', completedAt: '2026-01-01T00:00:10Z', outcome: 'Failed', error: 'Webhook delivery failed' }] }
        else if (path.endsWith('/webhook')) body = { status: retried ? 'Pending' : 'Exhausted', attemptCount: 10, cycleAttemptCount: retried ? 0 : 10, nextAttemptAt: null, leasedUntil: null, lastError: retried ? null : 'Webhook delivery failed', canRetry: !retried }
        else if (path === '/v1/alerts/rules') body = { ...body, items: [{ id: 'rule-1', contractId: 'contract-1', name: 'Old obligation', signal: 'DeadLetters', threshold: 1, reminderSeconds: 0, enabled: !archived, active: true, archived, webhookUrl: null, webhookSecretRef: null }] }
        else if (path === '/v1/alerts/notifications') body = { ...body, items: [{ id: 'notice-1', contractId: 'contract-1', state: 'Firing', message: 'Failure notification', createdAt: '2026-01-01T00:00:00Z', acknowledgedAt: null }] }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/alerts`)
      await page.getByRole('button', { name: 'Webhook delivery', exact: true }).click()
      const dialog = page.getByRole('dialog', { name: 'Webhook delivery', exact: true })
      await dialog.getByText('Status: Exhausted', { exact: true }).waitFor()
      await dialog.getByRole('table', { name: 'Webhook attempts' }).getByText('Failed', { exact: true }).waitFor()
      assert.equal(await dialog.getByRole('table', { name: 'Webhook attempts' }).getByText('Failed', { exact: true }).count(), 1)
      if (role === 'Viewer') {
        assert.equal(await dialog.getByRole('button', { name: 'Retry webhook', exact: true }).count(), 0)
        await dialog.getByRole('button', { name: 'Close dialog' }).click()
        assert.equal(await page.getByRole('button', { name: 'Archive', exact: true }).isDisabled(), true)
        assert.equal(retried, false); assert.equal(archived, false)
        return
      }
      await dialog.getByRole('button', { name: 'Retry webhook', exact: true }).click()
      await page.getByRole('alertdialog').getByRole('button', { name: 'Retry webhook', exact: true }).click()
      await dialog.getByText('Status: Pending', { exact: true }).waitFor()
      assert.match(operationId, /^[0-9a-f-]{36}$/)
      assert.equal(await dialog.getByRole('button', { name: 'Retry webhook', exact: true }).isDisabled(), true)
      await dialog.getByRole('button', { name: 'Close dialog' }).click()
      await page.getByRole('button', { name: 'Archive', exact: true }).click()
      await page.getByRole('alertdialog').getByRole('button', { name: 'Archive rule', exact: true }).click()
      await page.getByRole('row').filter({ hasText: 'Old obligation' }).getByText('Archived', { exact: true }).waitFor()
      assert.equal(archived, true)
      assert.equal(await page.getByRole('button', { name: 'Archive', exact: true }).count(), 0)
    } finally { await browser.close() }
  })
}
