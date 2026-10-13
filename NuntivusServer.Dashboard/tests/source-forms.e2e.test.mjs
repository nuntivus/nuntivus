import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

async function openApp(permissions = ['contracts.read', 'contracts.write']) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  let source = {
    id: 'source-1', name: 'Original source', type: 'HTTP_API', description: null, enabled: true,
    configuration: { authentication: { type: 'none' }, method: 'POST' },
    createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
  }
  const requests = []
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const url = new URL(route.request().url())
    const method = route.request().method()
    let body = {}
    if (url.pathname === '/v1/admin/users/me') {
      body = { subject: 'test-admin', roles: ['Admin'], permissions }
    } else if (url.pathname === '/v1/sources' && method === 'GET') {
      body = { items: [source], page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasNext: false }
    } else if (url.pathname === '/v1/sources' && method === 'POST') {
      const input = route.request().postDataJSON()
      requests.push(input)
      source = { ...source, ...input }
      body = source
    } else if (url.pathname === '/v1/sources/source-1' && method === 'PUT') {
      const input = route.request().postDataJSON()
      requests.push(input)
      source = { ...source, ...input, updatedAt: '2026-01-02T00:00:00Z' }
      body = source
    } else if (url.pathname === '/v1/sources/source-1') {
      body = source
    } else if (url.pathname === '/v1/admin/secrets') {
      body = []
    } else if (url.pathname === '/v1/destinations/destination-1') {
      body = { id: 'destination-1', name: 'Webhook', type: 1, configured: true, enabled: true,
        configuration: JSON.stringify({ Url: 'https://example.com/hook', Authentication: { Type: 'none' } }),
        createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page, requests }
}

test('source Add opens a dedicated page and saves to Source detail', async () => {
  const { browser, page, requests } = await openApp()
  try {
    await page.goto(`${appUrl}/sources`)
    await page.getByRole('link', { name: 'Add Source' }).click()
    assert.match(page.url(), /\/sources\/new$/)
    await page.getByRole('textbox', { name: /Name/ }).fill('New source')
    await page.getByRole('button', { name: 'Create source' }).click()
    await page.waitForURL(/\/sources\/source-1$/)
    await page.getByRole('heading', { name: 'New source' }).waitFor()
    assert.deepEqual(requests[0].configuration, { authentication: { type: 'none' }, method: 'POST' })
    assert.match(page.url(), /\/sources\/source-1$/)
  } finally { await browser.close() }
})

test('source list Edit opens the detail form and saves changes in place', async () => {
  const { browser, page, requests } = await openApp()
  try {
    await page.goto(`${appUrl}/sources`)
    await page.getByRole('link', { name: 'Edit' }).click()
    await page.getByRole('heading', { name: 'HTTP Source Configuration' }).waitFor()
    assert.match(page.url(), /\/sources\/source-1\?edit=1$/)
    assert.equal(await page.getByRole('tab', { name: 'Configuration' }).getAttribute('aria-selected'), 'true')
    await page.getByRole('tab', { name: 'Overview' }).click()
    await page.getByRole('heading', { name: 'Source Overview' }).waitFor()
    assert.equal(await page.getByRole('heading', { name: 'HTTP Source Configuration' }).isVisible(), false)
    await page.getByRole('tab', { name: 'Events' }).click()
    await page.getByRole('heading', { name: 'HTTP ingestion' }).waitFor()
    assert.equal(await page.getByRole('heading', { name: 'Source Overview' }).isVisible(), false)
    await page.getByRole('tab', { name: 'Configuration' }).click()
    const nameBox = await page.getByRole('textbox', { name: /Name/ }).boundingBox()
    const typeBox = await page.getByRole('combobox', { name: 'Source type' }).boundingBox()
    assert.ok(nameBox && typeBox && typeBox.x > nameBox.x)
    const label = page.locator('.source-configuration-card .settings-editor-field').first()
    const nameText = await label.locator('span').first().boundingBox()
    const requiredMarker = await label.locator('span[aria-hidden="true"]').boundingBox()
    assert.ok(nameText && requiredMarker && requiredMarker.y >= nameText.y && requiredMarker.y < nameText.y + nameText.height)
    assert.equal(await page.getByRole('tab', { name: 'Settings' }).count(), 0)
    await page.getByRole('textbox', { name: /Name/ }).fill('Updated source')
    await page.getByText('Advanced Settings', { exact: true }).click()
    await page.getByRole('combobox', { name: 'Source authentication' }).waitFor()
    await page.getByRole('tab', { name: 'Overview' }).click()
    await page.getByRole('tab', { name: 'Configuration' }).click()
    assert.equal(await page.getByRole('textbox', { name: /Name/ }).inputValue(), 'Updated source')
    await page.getByRole('button', { name: 'Save changes' }).click()
    await page.getByRole('tab', { name: 'Overview' }).waitFor()
    await page.getByRole('heading', { name: 'Source Overview' }).waitFor()
    assert.equal(requests[0].name, 'Updated source')
    assert.doesNotMatch(page.url(), /edit=1/)
  } finally { await browser.close() }
})

test('source creation and editing require write permission', async () => {
  const { browser, page } = await openApp(['contracts.read'])
  try {
    await page.goto(`${appUrl}/sources/new`)
    await page.getByText(/permission|access denied|forbidden/i).first().waitFor()
    assert.equal(await page.getByRole('button', { name: 'Create source' }).count(), 0)
    await page.goto(`${appUrl}/sources/source-1?edit=1`)
    await page.getByRole('heading', { name: 'Source Overview' }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Save changes' }).count(), 0)
  } finally { await browser.close() }
})

test('destination detail editor uses the Source layout and keeps edits across tabs', async () => {
  const { browser, page } = await openApp()
  try {
    await page.goto(`${appUrl}/destinations/destination-1?edit=1`)
    await page.getByRole('heading', { name: 'Http Destination Configuration' }).waitFor()
    assert.equal(await page.getByRole('tab', { name: 'Configuration' }).getAttribute('aria-selected'), 'true')
    const name = await page.getByRole('textbox', { name: /Name/ }).boundingBox()
    const type = await page.getByRole('textbox', { name: 'Type' }).boundingBox()
    assert.ok(name && type && type.x > name.x)
    await page.getByRole('textbox', { name: /Name/ }).fill('Updated destination')
    await page.getByRole('tab', { name: 'Overview' }).click()
    await page.getByRole('heading', { name: 'Destination Overview' }).waitFor()
    assert.equal(await page.getByRole('heading', { name: 'Http Destination Configuration' }).isVisible(), false)
    await page.getByRole('tab', { name: 'Configuration' }).click()
    assert.equal(await page.getByRole('textbox', { name: /Name/ }).inputValue(), 'Updated destination')
    await page.getByText('Advanced Settings', { exact: true }).click()
    const url = await page.getByRole('textbox', { name: 'Destination URL' }).boundingBox()
    assert.ok(url && name && url.y > name.y)
    await page.getByRole('button', { name: 'Add header' }).click()
    await page.getByRole('textbox', { name: 'Header 1 key' }).fill('X-Region')
    await page.getByRole('textbox', { name: 'Header 1 value' }).fill('west')
    await page.getByRole('button', { name: 'Add header' }).click()
    await page.getByRole('textbox', { name: 'Header 2 key' }).fill('X-Trace')
    await page.getByRole('button', { name: 'Remove header 1' }).click()
    assert.equal(await page.getByRole('textbox', { name: 'Header 1 key' }).inputValue(), 'X-Trace')
    assert.equal(await page.getByRole('textbox', { name: 'Header 1 value' }).inputValue(), '')
    assert.equal(await page.getByRole('tab', { name: 'Settings' }).count(), 0)
  } finally { await browser.close() }
})

test('destination read-only visitors can view configuration but cannot edit', async () => {
  const { browser, page } = await openApp(['contracts.read'])
  try {
    await page.goto(`${appUrl}/destinations/destination-1?edit=1`)
    await page.getByRole('heading', { name: 'Destination Overview' }).waitFor()
    await page.getByRole('tab', { name: 'Configuration' }).click()
    await page.getByRole('heading', { name: 'Http configuration' }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Save changes' }).count(), 0)
  } finally { await browser.close() }
})
