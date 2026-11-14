import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const timestamp = '2026-01-01T00:00:00Z'
const paged = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, hasNext: false })

async function openApp(kind, permissions = ['contracts.read', 'contracts.write'], failFirstCreate = false) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  const collection = kind === 'Source' ? 'sources' : 'destinations'
  const writes = []
  let item
  let rejectCreate = failFirstCreate
  await page.addInitScript(() => sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    let body = []
    if (path === '/v1/admin/users/me') body = { subject: 'creation-admin', roles: ['Admin'], permissions }
    else if (path === `/v1/${collection}` && request.method() === 'POST') {
      const input = request.postDataJSON()
      writes.push(input)
      if (rejectCreate) {
        rejectCreate = false
        await route.fulfill({ status: 422, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Create failed', detail: 'Unable to create this integration.' }) })
        return
      }
      item = { ...input, id: 'integration-1', enabled: true, createdAt: timestamp, updatedAt: timestamp }
      if (kind === 'Destination') item = { ...item, type: 1, configured: true, disablePolicy: 'continue', configuration: JSON.stringify(input.configuration) }
      body = item
    } else if (path === `/v1/${collection}`) body = paged(item ? [item] : [])
    else if (path === `/v1/${collection}/integration-1`) body = item
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page, writes, collection }
}

for (const kind of ['Source', 'Destination']) {
  test(`${kind} uses a dedicated creation page and creates with configuration in one request`, async () => {
    const { browser, page, writes, collection } = await openApp(kind)
    try {
      await page.goto(`${appUrl}/${collection}`)
      await page.getByRole('link', { name: `Add ${kind}`, exact: true }).click()
      await page.waitForURL(`${appUrl}/${collection}/new`)
      await page.getByRole('heading', { name: `Register a new ${kind.toLowerCase()}`, exact: true }).waitFor()
      assert.equal(await page.getByRole('dialog').count(), 0)
      const name = page.getByRole('textbox', { name: /Name/ })
      const create = page.getByRole('button', { name: `Create ${kind.toLowerCase()}`, exact: true })
      await create.click()
      assert.equal(writes.length, 0)
      await name.fill(`New ${kind}`)
      if (kind === 'Destination') {
        await create.click()
        await page.getByRole('alert').filter({ hasText: 'Destination URL is required.' }).waitFor()
        assert.equal(writes.length, 0)
        await page.getByRole('textbox', { name: 'Destination URL', exact: true }).fill('https://example.com/events')
      } else {
        await page.getByRole('radio', { name: 'Kafka', exact: true }).check()
        await create.click()
        await page.getByText(/Complete the name, required configuration fields/).waitFor()
        assert.equal(writes.length, 0)
        await page.getByRole('radio', { name: 'HTTP API', exact: true }).check()
      }
      await create.click()
      await page.waitForURL(`${appUrl}/${collection}/integration-1`)
      await page.getByRole('heading', { name: `New ${kind}`, exact: true }).waitFor()
      assert.equal(writes.length, 1)
      assert.equal(await page.getByRole('alertdialog').count(), 0)
      assert.equal(new URL(page.url()).search, '')
      if (kind === 'Source') assert.deepEqual(writes[0].configuration, { authentication: { type: 'none' }, method: 'POST' })
      else {
        assert.equal(writes[0].type, undefined)
        assert.equal(writes[0].configuration.type, 'http')
        assert.equal(writes[0].configuration.url, 'https://example.com/events')
      }
    } finally { await browser.close() }
  })

  test(`${kind} cancel and back actions use the shared unsaved-change flow`, async () => {
    const { browser, page, writes, collection } = await openApp(kind)
    try {
      await page.goto(`${appUrl}/${collection}/new`)
      await page.getByRole('button', { name: 'Cancel', exact: true }).click()
      await page.waitForURL(`${appUrl}/${collection}`)
      assert.equal(await page.getByRole('alertdialog').count(), 0)
      await page.getByRole('link', { name: `Add ${kind}`, exact: true }).click()
      await page.getByRole('textbox', { name: /Name/ }).fill('Unsaved integration')
      await page.getByRole('link', { name: `Back to ${kind}s`, exact: true }).click()
      const dialog = page.getByRole('alertdialog')
      await dialog.getByRole('button', { name: 'Stay', exact: true }).click()
      assert.equal(new URL(page.url()).pathname, `/${collection}/new`)
      assert.equal(await page.getByRole('textbox', { name: /Name/ }).inputValue(), 'Unsaved integration')
      await page.getByRole('button', { name: 'Cancel', exact: true }).click()
      await dialog.getByRole('button', { name: 'Leave page', exact: true }).click()
      await page.waitForURL(`${appUrl}/${collection}`)
      assert.equal(writes.length, 0)
    } finally { await browser.close() }
  })

  test(`${kind} creation requires write permission`, async () => {
    const { browser, page, writes, collection } = await openApp(kind, ['contracts.read'])
    try {
      await page.goto(`${appUrl}/${collection}`)
      await page.getByRole('heading', { name: `${kind}s`, exact: true }).waitFor()
      assert.equal(await page.getByRole('link', { name: `Add ${kind}`, exact: true }).count(), 0)
      await page.goto(`${appUrl}/${collection}/new`)
      await page.getByText(/permission|access denied|forbidden/i).first().waitFor()
      assert.equal(await page.getByRole('button', { name: `Create ${kind.toLowerCase()}`, exact: true }).count(), 0)
      assert.equal(writes.length, 0)
    } finally { await browser.close() }
  })

  test(`${kind} preserves configuration after a failed create and permits retry`, async () => {
    const { browser, page, writes, collection } = await openApp(kind, undefined, true)
    try {
      await page.goto(`${appUrl}/${collection}/new`)
      await page.getByRole('textbox', { name: /Name/ }).fill('Retry integration')
      if (kind === 'Destination') await page.getByRole('textbox', { name: 'Destination URL', exact: true }).fill('https://example.com/retry')
      const create = page.getByRole('button', { name: `Create ${kind.toLowerCase()}`, exact: true })
      await create.click()
      await page.getByText('Unable to create this integration.', { exact: true }).waitFor()
      assert.equal(new URL(page.url()).pathname, `/${collection}/new`)
      assert.equal(await page.getByRole('textbox', { name: /Name/ }).inputValue(), 'Retry integration')
      if (kind === 'Destination') assert.equal(await page.getByRole('textbox', { name: 'Destination URL', exact: true }).inputValue(), 'https://example.com/retry')
      await create.click()
      await page.waitForURL(`${appUrl}/${collection}/integration-1`)
      assert.equal(writes.length, 2)
      assert.deepEqual(writes[0], writes[1])
    } finally { await browser.close() }
  })
}
