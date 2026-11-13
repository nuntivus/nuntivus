import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
for (const role of ['Operator', 'Viewer']) {
  test(`${role} failed-delivery exports use applied filters`, async () => {
    const browser = await chromium.launch({ headless: true })
    try {
      const page = await browser.newPage({ acceptDownloads: true })
      const exports = []
      const contract = '7521772e-cc7a-4812-8eb2-b508e0b0fb33'
      await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const url = new URL(route.request().url())
        let body = { items: [], page: 1, pageSize: 20, totalCount: 0, hasNext: false }
        if (url.pathname === '/v1/admin/users/me') body = { subject: 'export-user', roles: [role], permissions: ['delivery-failures.read', 'deliveries.read', 'customers.read'] }
        else if (url.pathname === '/v1/customers') body = { ...body, items: [{ id: 'unapplied-customer', name: 'Unapplied customer' }] }
        else if (url.pathname === '/v1/exports/deliveries') {
          exports.push(url)
          await route.fulfill({ status: 200, contentType: 'text/csv', body: '"id"\r\n' })
          return
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/deliveries/failures?customerId=customer-1&contractId=${contract}&failureCategory=Timeout&status=Dead&page=2&pageSize=50&sortDirection=desc`)
      await page.getByRole('heading', { name: 'Failures', exact: true }).waitFor()
      if (role === 'Viewer') { assert.equal(await page.getByRole('button', { name: 'Export CSV', exact: true }).count(), 0); return }
      await page.getByRole('combobox', { name: 'Customer ID', exact: true }).click()
      await page.getByRole('option', { name: /Unapplied customer/ }).click()
      const download = page.waitForEvent('download')
      await page.getByRole('button', { name: 'Export CSV', exact: true }).click()
      assert.equal((await download).suggestedFilename(), 'deliveries.csv')
      assert.equal(exports.length, 1)
      for (const [key, value] of Object.entries({ customerId: 'customer-1', contractId: contract, failureCategory: 'Timeout', status: 'Dead', format: 'csv' })) assert.equal(exports[0].searchParams.get(key), value)
      for (const key of ['page', 'pageSize', 'sortDirection']) assert.equal(exports[0].searchParams.has(key), false)
    } finally { await browser.close() }
  })
}
