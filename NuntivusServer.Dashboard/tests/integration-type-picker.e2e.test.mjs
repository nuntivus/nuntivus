import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

for (const [kind, path, count, awsCount, nextType] of [
  ['Destination', '/destinations/new', 5, 1, 'NATS'],
  ['Source', '/sources/new', 8, 2, 'Amazon SQS'],
]) {
  test(`${kind} type cards support search, icons, retained selection, keyboard access and mobile layout`, async () => {
    const browser = await chromium.launch({ headless: true })
    try {
      const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
      let writes = 0
      await page.addInitScript(() => sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const request = route.request()
        if (request.method() === 'POST') writes++
        const requestPath = new URL(request.url()).pathname
        const body = requestPath === '/v1/admin/users/me'
          ? { subject: 'picker-admin', roles: ['Admin'], permissions: ['contracts.read', 'contracts.write'] }
          : requestPath === '/v1/admin/secrets' ? []
          : { items: [], page: 1, pageSize: 20, totalCount: 0, hasNext: false }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}${path}`)
      const group = page.getByRole('radiogroup', { name: `${kind} type`, exact: true })
      await group.waitFor()
      const typePanel = page.locator('.integration-creation-types')
      const configurationPanel = page.locator('.integration-creation-content')
      const desktopTypes = await typePanel.boundingBox()
      const desktopConfiguration = await configurationPanel.boundingBox()
      assert.ok(desktopTypes && desktopConfiguration && desktopConfiguration.x >= desktopTypes.x + desktopTypes.width)
      assert.ok(Math.abs(desktopTypes.y - desktopConfiguration.y) < 2)
      assert.equal(await typePanel.getByRole('radiogroup', { name: `${kind} type`, exact: true }).count(), 1)
      assert.equal(await configurationPanel.getByRole('textbox', { name: /Name/ }).count(), 1)
      assert.equal(await group.getByRole('radio').count(), count)
      assert.equal(await group.locator('.integration-type-icon svg').count(), count)
      assert.equal(await group.locator('input:checked').count(), 1)
      const picker = page.locator('.integration-type-picker')
      const search = page.getByRole('searchbox', { name: `Search ${kind.toLowerCase()} types`, exact: true })
      await search.fill('  KAFKA  ')
      assert.equal(await group.getByRole('radio').count(), 1)
      await group.getByRole('radio', { name: 'Kafka', exact: true }).check()
      await search.fill('aws')
      assert.equal(await group.getByRole('radio').count(), awsCount)
      assert.match(await picker.locator('.integration-type-help').innerText(), /Selected: Kafka/)
      await picker.getByRole('button', { name: 'Clear type search', exact: true }).click()
      assert.equal(await group.getByRole('radio', { name: 'Kafka', exact: true }).isChecked(), true)
      await search.fill('no-such-integration')
      assert.equal(await group.getByRole('radio').count(), 0)
      assert.match(await picker.getByRole('status').innerText(), /No integrations match/)
      await search.press('Enter')
      assert.equal(writes, 0)
      assert.equal(new URL(page.url()).pathname, path)
      await picker.getByRole('button', { name: 'Clear type search', exact: true }).click()
      assert.equal(await group.getByRole('radio', { name: 'Kafka', exact: true }).isChecked(), true)
      await group.getByRole('radio', { name: 'Kafka', exact: true }).focus()
      await page.keyboard.press('ArrowRight')
      assert.equal(await group.getByRole('radio', { name: nextType, exact: true }).isChecked(), true)
      assert.equal(writes, 0)

      for (const width of [768, 390, 320]) {
        await page.setViewportSize({ width, height: 844 })
        const types = await typePanel.boundingBox()
        const configuration = await configurationPanel.boundingBox()
        assert.ok(types && configuration && configuration.y >= types.y + types.height)
        assert.ok(Math.abs(types.x - configuration.x) < 2)
        const bounds = await group.boundingBox()
        assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= width)
        const first = await group.locator('.integration-type-card').nth(0).boundingBox()
        const second = await group.locator('.integration-type-card').nth(1).boundingBox()
        assert.ok(first && second && Math.abs(first.x - second.x) < 1 && second.y >= first.y + first.height)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true)
      }

      await page.setViewportSize({ width: 1280, height: 900 })
      await search.fill('Kafka')
      await page.getByRole('button', { name: 'Cancel', exact: true }).click()
      await page.getByRole('alertdialog').getByRole('button', { name: 'Leave page', exact: true }).click()
      await page.waitForURL(`${appUrl}${kind === 'Destination' ? '/destinations' : '/sources'}`)
      await page.getByRole('link', { name: `Add ${kind}`, exact: true }).click()
      await group.waitFor()
      assert.equal(await search.inputValue(), '')
      assert.equal(await group.getByRole('radio', { name: kind === 'Destination' ? 'HTTP' : 'HTTP API', exact: true }).isChecked(), true)
    } finally { await browser.close() }
  })
}
