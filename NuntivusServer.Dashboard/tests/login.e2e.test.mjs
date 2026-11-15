import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('login branding and labeled fields fit desktop and mobile screens', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    await page.goto(`${appUrl}/login`)
    await page.getByRole('heading', { name: 'Admin login' }).waitFor()
    const logo = page.getByRole('img', { name: 'Nuntivus logo' })
    assert.equal(await logo.evaluate(image => image.complete && image.naturalWidth > 0), true)
    const pixels = await logo.evaluate(image => {
      const canvas = document.createElement('canvas')
      canvas.width = 605
      canvas.height = 485
      const context = canvas.getContext('2d')
      context.drawImage(image, 0, 0, 605, 485)
      return {
        upperRight: [...context.getImageData(450, 55, 1, 1).data],
        letter: [...context.getImageData(70, 145, 1, 1).data],
        arrow: [...context.getImageData(400, 175, 1, 1).data],
      }
    })
    assert.equal(pixels.upperRight[3], 0, 'The N must not protrude above the arrow')
    assert.equal(pixels.letter[3], 255)
    assert.equal(pixels.arrow[3], 255)
    assert.ok(pixels.arrow[2] > pixels.letter[2])
    assert.equal(await page.getByLabel('Username', { exact: true }).getAttribute('autocomplete'), 'username')
    assert.equal(await page.getByLabel('Password', { exact: true }).getAttribute('autocomplete'), 'current-password')
    assert.equal(await page.getByLabel('Password', { exact: true }).getAttribute('type'), 'password')
    assert.equal(await page.getByRole('button', { name: 'Sign in', exact: true }).isEnabled(), true)
    assert.equal(await page.getByRole('alert').count(), 0)
    assert.equal(await page.locator('[aria-invalid="true"]').count(), 0)

    for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 320, height: 568 }]) {
      await page.setViewportSize(viewport)
      const bounds = await page.locator('.login-card').boundingBox()
      assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= viewport.width)
      assert.ok(Math.abs(bounds.x + bounds.width / 2 - viewport.width / 2) < 2)
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true)
    }
  } finally {
    await browser.close()
  }
})

test('login validates on submit and clears field feedback as values are corrected', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    const requests = []
    await page.route('**/v1/auth/local/login', async route => {
      requests.push(route.request().postDataJSON())
      await route.fulfill({ status: 401, contentType: 'application/json', body: '{}' })
    })
    await page.goto(`${appUrl}/login`)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await page.getByText('Username is required.', { exact: true }).waitFor()
    assert.equal(await page.getByText('Password is required.', { exact: true }).isVisible(), true)
    assert.equal(requests.length, 0)
    await page.getByLabel('Username', { exact: true }).fill('test.admin')
    await page.getByText('Username is required.', { exact: true }).waitFor({ state: 'hidden' })
    await page.getByLabel('Password', { exact: true }).fill('test-password')
    assert.equal(await page.getByRole('alert').count(), 0)
    assert.equal(await page.locator('[aria-invalid="true"]').count(), 0)
  } finally { await browser.close() }
})

test('login submits browser-autofilled values without requiring change events', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    let credentials
    await page.route('**/v1/auth/local/login', async route => {
      credentials = route.request().postDataJSON()
      await route.fulfill({ status: 401, contentType: 'application/json', body: '{}' })
    })
    await page.goto(`${appUrl}/login`)
    await page.getByRole('heading', { name: 'Admin login' }).waitFor()
    await page.evaluate(() => {
      document.querySelector('[name="username"]').value = 'autofilled.admin'
      document.querySelector('[name="password"]').value = 'autofilled-password'
    })
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await page.getByRole('alertdialog').waitFor()
    assert.deepEqual(credentials, { username: 'autofilled.admin', password: 'autofilled-password' })
    assert.equal(await page.locator('[aria-invalid="true"]').count(), 0)
  } finally { await browser.close() }
})

test('login submits credentials by keyboard and preserves the failure dialog', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage()
    let credentials
    await page.route('**/v1/auth/local/login', async route => {
      credentials = route.request().postDataJSON()
      await route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Invalid credentials', detail: 'Incorrect username or password.' }),
      })
    })
    await page.goto(`${appUrl}/login`)
    await page.getByLabel('Username', { exact: true }).fill('test.admin')
    await page.getByLabel('Password', { exact: true }).fill('test-password')
    assert.equal(await page.getByRole('button', { name: 'Sign in', exact: true }).isEnabled(), true)
    await page.getByLabel('Password', { exact: true }).press('Enter')
    await page.getByRole('alertdialog').waitFor()
    assert.deepEqual(credentials, { username: 'test.admin', password: 'test-password' })
    assert.equal(await page.getByRole('heading', { name: 'Login failed' }).isVisible(), true)
    await page.getByRole('button', { name: 'Dismiss', exact: true }).click()
    await page.getByRole('alertdialog').waitFor({ state: 'hidden' })
    assert.equal(await page.getByLabel('Username', { exact: true }).inputValue(), 'test.admin')
  } finally {
    await browser.close()
  }
})
