import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('preferences initialize per account and preserve save/cancel/reset behavior', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    let subject = 'preferences-one'
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      const body = path === '/v1/admin/users/me'
        ? { subject, roles: ['Viewer'], permissions: [] }
        : { items: [], page: 1, pageSize: 20, totalCount: 0, hasNext: false }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/settings`)
    const theme = page.getByRole('combobox', { name: 'Theme', exact: true })
    await theme.waitFor()
    assert.equal(await theme.innerText(), 'Light')
    await theme.click()
    await page.getByRole('option', { name: 'Dark', exact: true }).click()
    assert.equal(await page.getByRole('button', { name: 'Save preferences', exact: true }).isEnabled(), true)
    await page.getByRole('button', { name: 'Cancel', exact: true }).click()
    assert.equal(await theme.innerText(), 'Light')
    await theme.click()
    await page.getByRole('option', { name: 'Dark', exact: true }).click()
    await page.getByRole('button', { name: 'Save preferences', exact: true }).click()
    await page.reload()
    await theme.waitFor()
    assert.equal(await theme.innerText(), 'Dark')
    subject = 'preferences-two'
    await page.reload()
    await theme.waitFor()
    assert.equal(await theme.innerText(), 'Light')
    await theme.click()
    await page.getByRole('option', { name: 'Dark', exact: true }).click()
    await page.getByRole('button', { name: 'Reset to defaults', exact: true }).click()
    assert.equal(await theme.innerText(), 'Light')
    assert.equal(await page.evaluate(() => JSON.parse(window.localStorage.getItem('nuntivus.dashboard.preferences.v1.preferences-one')).theme), 'dark')
  } finally { await browser.close() }
})
