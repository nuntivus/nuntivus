import assert from 'node:assert/strict'
import { createRequire } from 'node:module'

// Reuse the dashboard's installed browser tooling without coupling website production dependencies to it.
const require = createRequire(new URL('../../NuntivusServer.Dashboard/package.json', import.meta.url))
const { chromium } = require('playwright')
const url = process.env.WEBSITE_TEST_URL ?? 'http://localhost:5180'
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.goto(url)
  await page.getByRole('heading', { level: 1, name: /Your events/ }).waitFor()
  await page.getByRole('note', { name: 'Alpha release status' }).waitFor()
  assert.match(await page.locator('.navbar__title').first().innerText(), /Nuntivus α/)
  assert.match(await page.getByRole('note', { name: 'Alpha release status' }).innerText(), /Not ready for production/)
  assert.equal(await page.locator('link[rel="canonical"]').getAttribute('href'), 'https://nuntivus.com/')
  assert.ok(await page.locator('a[href="https://github.com/nuntivus/nuntivus-server"]').count() >= 2)
  const architecture = page.locator('.architecture-viewport img')
  await architecture.scrollIntoViewIfNeeded()
  await architecture.evaluate(image => image.decode())
  assert.equal(await architecture.evaluate(image => image.naturalWidth), 1200)
  await page.getByRole('link', { name: 'Get started →', exact: true }).click()
  await page.getByRole('heading', { level: 1, name: 'Local quick start' }).waitFor()
  await page.goto(`${url}/docs/reference/architecture/`)
  await page.locator('article h1').waitFor()
  const documentationDiagram = page.getByRole('img', { name: /^Nuntivus architecture:/ })
  await documentationDiagram.evaluate(image => image.decode())
  assert.equal(await documentationDiagram.count(), 1)
  await page.goto(`${url}/blog/`)
  await page.getByRole('link', { name: 'Introducing Nuntivus', exact: true }).first().click()
  await page.getByRole('heading', { level: 1, name: 'Introducing Nuntivus' }).waitFor()
  await page.goto(url)
  const search = page.getByRole('textbox', { name: /search/i }).first()
  await search.fill('retry')
  await page.waitForFunction(() => document.querySelectorAll('[class*="suggestion"], [class*="Suggestion"]').length > 0)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(url)
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
  assert.equal(await page.locator('.architecture-viewport').evaluate(element => element.scrollWidth > element.clientWidth), true)
  await page.goto(`${url}/img/docs/assets/architecture.svg`)
  const overflowingLabels = await page.locator('svg text').evaluateAll(elements => elements.filter(element => {
    const box = element.getBBox()
    const containers = [...document.querySelectorAll('rect')].filter(rectangle => {
      const bounds = rectangle.getBBox()
      return bounds.width < 1200 && box.x >= bounds.x && box.x < bounds.x + bounds.width && box.y >= bounds.y && box.y + box.height <= bounds.y + bounds.height
    })
    const container = containers.sort((a, b) => a.getBBox().width - b.getBBox().width)[0]
    return container && box.x + box.width > container.getBBox().x + container.getBBox().width - 6
  }).map(element => element.textContent))
  assert.deepEqual(overflowingLabels, [])
  await page.goto(url)
  await page.getByRole('button', { name: /toggle navigation/i }).click()
  await page.locator('.navbar-sidebar').getByRole('link', { name: 'Documentation', exact: true }).click()
  await page.locator('article h1').waitFor()
  assert.deepEqual(errors, [])
  console.log('Website smoke checks passed: homepage, canonical URL, GitHub links, docs, blog, search, mobile navigation and overflow.')
} finally { await browser.close() }
