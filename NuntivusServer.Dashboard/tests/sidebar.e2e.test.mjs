import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

async function mockSession(page, permissions) {
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const path = new URL(route.request().url()).pathname
    const body = path === '/v1/admin/users/me'
      ? { subject: 'sidebar-user', roles: ['Viewer'], permissions }
      : path === '/v1/admin/settings/instance'
      ? {
        deliveryDefaults: { requestTimeoutSeconds: 30, maxAttempts: 5, batchLimit: 100 },
        retryDefaults: { initialDelaySeconds: 1, maxDelaySeconds: 300, backoffMultiplier: 2 },
        security: { requireHttpsDestinations: true, allowPrivateNetworkDestinations: false, requireDestinationCredentialEncryption: true },
        retention: { eventRetentionDays: 30, deliveryAuditRetentionDays: 30, operationAuditRetentionDays: 90 },
        limits: { maxEventPayloadBytes: 1048576, maxEventMetadataBytes: 16384, maxContractsPerCustomer: 100, maxConcurrentReplayJobs: 5 },
        updatedAt: '2026-01-01T00:00:00Z', updatedBy: null,
      }
      : { items: [], page: 1, pageSize: 20, totalCount: 0, hasNext: false }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

test('workflow sidebar groups collapse accessibly and keep Settings visible', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
    await mockSession(page, ['customers.read', 'contracts.read', 'contracts.write', 'events.read', 'event-definitions.read', 'delivery-failures.read', 'audit-logs.read', 'users.read', 'settings.write'])
    await page.goto(`${appUrl}/deliveries/failures`)
    const nav = page.getByRole('navigation', { name: 'Primary navigation', exact: true })
    await nav.getByRole('link', { name: 'Failed Deliveries', exact: true }).waitFor()
    const brand = page.locator('.sidebar-brand')
    const logo = brand.locator('img')
    await page.waitForFunction(() => {
      const image = document.querySelector('.sidebar-brand img')
      return image?.complete && image.naturalWidth > 0
    })
    const expectedLogo = await page.evaluate(async () => {
      const { default: url } = await import('/src/assets/nuntivus-logo.svg?import')
      return new URL(url, document.baseURI).href
    })
    assert.equal(await logo.evaluate(image => image.currentSrc), expectedLogo)
    assert.equal(await brand.innerText(), 'Nuntivus Admin')

    const groups = {
      'Event flow': ['Sources', 'Event Definitions', 'Contracts', 'Destinations', 'Routing map'],
      Monitoring: ['Events', 'Deliveries', 'Failed Deliveries', 'Alerts'],
      Tools: ['Test events', 'Secrets'],
      Administration: ['Customers', 'Users', 'Audit Logs'],
    }
    assert.deepEqual(await nav.getByRole('button').allTextContents(), Object.keys(groups))
    for (const [heading, labels] of Object.entries(groups)) {
      const button = nav.getByRole('button', { name: heading, exact: true })
      assert.equal(await button.getAttribute('aria-expanded'), 'true')
      const items = page.locator(`#${await button.getAttribute('aria-controls')}`)
      assert.deepEqual(await items.getByRole('link').allTextContents(), labels)
    }
    assert.deepEqual(await nav.locator('a[aria-current="page"]').allTextContents(), ['Failed Deliveries'])
    const flow = nav.getByRole('button', { name: 'Event flow', exact: true })
    await flow.focus()
    await page.keyboard.press('Enter')
    assert.equal(await flow.getAttribute('aria-expanded'), 'false')
    assert.equal(await nav.getByRole('link', { name: 'Sources', exact: true }).count(), 0)
    await page.keyboard.press('Space')
    assert.equal(await flow.getAttribute('aria-expanded'), 'true')
    await nav.getByRole('link', { name: 'Sources', exact: true }).waitFor()

    await nav.getByRole('link', { name: 'Deliveries', exact: true }).click()
    await page.waitForURL(`${appUrl}/deliveries`)
    await nav.locator('a[href="/deliveries"][aria-current="page"]').waitFor()
    assert.deepEqual(await nav.locator('a[aria-current="page"]').allTextContents(), ['Deliveries'])

    await page.setViewportSize({ width: 1280, height: 480 })
    const settings = page.getByRole('navigation', { name: 'Settings navigation', exact: true }).getByRole('link', { name: 'Settings', exact: true })
    const bounds = await settings.boundingBox()
    assert.ok(bounds && bounds.y >= 0 && bounds.y + bounds.height <= 480)
    assert.equal(await nav.getByRole('link', { name: 'Settings', exact: true }).count(), 0)
    await settings.click()
    await page.waitForURL(`${appUrl}/settings`)
    await page.locator('.sidebar-footer a[aria-current="page"]').waitFor()
    assert.equal(await settings.getAttribute('aria-current'), 'page')
    assert.equal(await nav.locator('a[aria-current="page"]').count(), 0)

    await page.getByRole('button', { name: 'Hide menu', exact: true }).click()
    await page.setViewportSize({ width: 390, height: 700 })
    await page.getByRole('button', { name: 'Show menu', exact: true }).click()
    await nav.getByRole('link', { name: 'Deliveries', exact: true }).click()
    await page.waitForURL(`${appUrl}/deliveries`)
    assert.equal(await page.locator('.dashboard-sidebar').evaluate(element => element.classList.contains('closed')), true)
  } finally {
    await browser.close()
  }
})

test('desktop hamburger hides and restores sidebar and expands content', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
    await mockSession(page, [])
    await page.goto(`${appUrl}/deliveries`)
    const sidebar = page.locator('#dashboard-sidebar')
    await page.getByRole('navigation', { name: 'Primary navigation', exact: true }).waitFor()
    const before = await page.locator('.dashboard-main').boundingBox()
    const hide = page.getByRole('button', { name: 'Hide menu', exact: true })
    assert.equal(await hide.getAttribute('aria-expanded'), 'true')
    await hide.click()
    assert.equal(await sidebar.isVisible(), false)
    const after = await page.locator('.dashboard-main').boundingBox()
    assert.ok(after.width > before.width + 200)
    const show = page.getByRole('button', { name: 'Show menu', exact: true })
    assert.equal(await show.getAttribute('aria-expanded'), 'false')
    await show.focus()
    await page.keyboard.press('Enter')
    assert.equal(await sidebar.isVisible(), true)
    assert.equal(await page.getByRole('button', { name: 'Hide menu', exact: true }).getAttribute('aria-expanded'), 'true')
  } finally { await browser.close() }
})

test('sidebar preserves permission-disabled links', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
    await mockSession(page, [])
    await page.goto(`${appUrl}/deliveries`)
    const nav = page.getByRole('navigation', { name: 'Primary navigation', exact: true })
    await nav.getByRole('link', { name: 'Deliveries', exact: true }).waitFor()
    assert.equal(await nav.getByRole('link', { name: 'Sources', exact: true }).count(), 0)
    assert.equal(await nav.locator('.sidebar-link.disabled', { hasText: 'Sources' }).getAttribute('aria-disabled'), 'true')
    const footer = page.getByRole('navigation', { name: 'Settings navigation', exact: true })
    assert.equal(await footer.getByRole('link').count(), 0)
    assert.equal(await footer.locator('.sidebar-link.disabled').innerText(), 'Settings')
  } finally {
    await browser.close()
  }
})
