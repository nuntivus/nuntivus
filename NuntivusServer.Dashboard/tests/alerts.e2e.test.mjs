import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('alert inbox acknowledges notifications and creates configurable reminder rules', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
    const writes = []
    let acknowledged = false
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      let body = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0, hasNext: false }
      if (path === '/v1/admin/users/me') body = { subject: 'alert-operator', roles: ['Operator'], permissions: ['contracts.read', 'contracts.write'] }
      else if (path === '/v1/admin/contracts') body = { ...body, items: [{ id: '7521772e-cc7a-4812-8eb2-b508e0b0fb33', name: 'Orders webhook' }] }
      else if (path.endsWith('/acknowledge')) { acknowledged = true; body = {} }
      else if (path === '/v1/alerts/notifications') body = { ...body, items: [{ id: 'notice-1', contractId: 'contract-1', state: 'Firing', message: 'Dead letters exceeded', createdAt: '2026-01-01T00:00:00Z', acknowledgedAt: acknowledged ? '2026-01-01T01:00:00Z' : null }] }
      else if (route.request().method() === 'PUT') { writes.push(route.request().postDataJSON()); body = {} }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/alerts`)
    await page.getByText('Dead letters exceeded', { exact: false }).waitFor()
    await page.getByRole('button', { name: 'Acknowledge', exact: true }).click()
    await page.getByRole('button', { name: 'Acknowledged', exact: true }).waitFor()
    assert.equal(acknowledged, true)
    assert.equal(await page.locator('[aria-label="Alert rules"] table').count(), 1)
    assert.equal(await page.getByRole('dialog', { name: 'Create rule', exact: true }).count(), 0)
    await page.getByRole('button', { name: 'Add rule', exact: true }).click()
    const details = page.getByRole('region', { name: 'Rule details', exact: true })
    await page.waitForFunction(() => {
      const fields = document.querySelectorAll('#alert-create-form .configuration-grid-2 .field')
      return fields.length >= 2 && Math.abs(fields[0].getBoundingClientRect().y - fields[1].getBoundingClientRect().y) < 2
    })
    const [contractBounds, nameBounds] = await details.locator('.field').evaluateAll(fields =>
      fields.map(field => {
        const { y, width } = field.getBoundingClientRect()
        return { y, width }
      }))
    assert.ok(Math.abs(contractBounds.y - nameBounds.y) < 2)
    assert.ok(contractBounds.width > 300 && nameBounds.width > 300)
    assert.equal(await page.locator('form .configuration-section').count(), 3)
    assert.equal(await page.locator('.form-dialog-footer').count(), 1)
    await page.getByRole('combobox', { name: 'Contract ID', exact: true }).click()
    await page.getByRole('option', { name: /Orders webhook/ }).click()
    await page.getByLabel('Rule name', { exact: true }).fill('Dead letter warning')
    await page.getByLabel('Reminder interval in seconds (0 disables)', { exact: true }).fill('300')
    await page.getByRole('button', { name: 'Create alert rule', exact: true }).click()
    await page.getByRole('dialog', { name: 'Create rule', exact: true }).waitFor({ state: 'hidden' })
    assert.equal(writes.length, 1)
    assert.equal(writes[0].contractId, '7521772e-cc7a-4812-8eb2-b508e0b0fb33')
    assert.equal(writes[0].reminderSeconds, 300)
    assert.equal(writes[0].signal, 'DeadLetters')
    assert.equal(writes[0].webhookUrl, null)
    await page.getByRole('button', { name: 'Add rule', exact: true }).click()
    await page.setViewportSize({ width: 390, height: 844 })
    await details.waitFor()
    const mobileFields = await details.locator('.field').evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top))
    assert.ok(mobileFields[1] > mobileFields[0])
    assert.equal(await page.locator('form .configuration-section').evaluateAll(elements => elements.every(element => element.scrollWidth <= element.clientWidth + 1)), true)
  } finally { await browser.close() }
})
