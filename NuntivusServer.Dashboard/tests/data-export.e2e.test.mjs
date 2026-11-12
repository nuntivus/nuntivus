import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
for (const role of ['Operator', 'Viewer']) {
  test(`${role} export controls respect permissions and applied filters`, async () => {
    const browser = await chromium.launch({ headless: true })
    try {
      const page = await browser.newPage({ acceptDownloads: true })
      const exports = []
      await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const url = new URL(route.request().url())
        let body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNext: false }
        if (url.pathname === '/v1/admin/users/me') body = { subject: 'export-user', roles: [role], permissions: ['events.read'] }
        else if (url.pathname === '/v1/exports/events') {
          exports.push({ url, authorization: route.request().headers().authorization })
          await route.fulfill({ status: 200, contentType: 'text/csv', body: '"id","type"\r\n"example","orders.created"\r\n' })
          return
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/events?type=orders.created&page=2&pageSize=50&sortDirection=desc`)
      await page.getByRole('heading', { name: 'Events', exact: true }).waitFor()
      if (role === 'Viewer') { assert.equal(await page.getByRole('button', { name: 'Export CSV', exact: true }).count(), 0); return }
      const download = page.waitForEvent('download')
      await page.getByRole('button', { name: 'Export CSV', exact: true }).click()
      assert.equal((await download).suggestedFilename(), 'events.csv')
      assert.equal(exports.length, 1)
      assert.equal(exports[0].authorization, 'Bearer playwright-token')
      assert.equal(exports[0].url.searchParams.get('type'), 'orders.created')
      assert.equal(exports[0].url.searchParams.get('format'), 'csv')
      assert.equal(exports[0].url.searchParams.has('page'), false)
      assert.equal(exports[0].url.searchParams.has('sortDirection'), false)
    } finally { await browser.close() }
  })
}
