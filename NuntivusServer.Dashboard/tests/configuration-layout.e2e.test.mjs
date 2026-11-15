import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('destination connection fields fill responsive columns for every destination type', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      const body = path === '/v1/admin/users/me'
        ? { subject: 'destination-layout', roles: ['Admin'], permissions: ['contracts.read', 'contracts.write'] }
        : path === '/v1/admin/secrets' ? [] : { items: [], totalCount: 0 }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/destinations/new`)
    const types = page.getByRole('radiogroup', { name: 'Destination type' })
    await types.waitFor()
    const details = page.locator('.settings-editor-fields').first()
    const nameBounds = await details.locator('label').first().boundingBox()
    const descriptionBounds = await details.locator('label').last().boundingBox()
    const detailsBounds = await details.boundingBox()
    assert.ok(descriptionBounds.y > nameBounds.y, 'Description appears below Name, matching Source forms')
    assert.ok(Math.abs(descriptionBounds.width - detailsBounds.width) < 2, 'Description spans the full details width')
    const grid = page.locator('.configuration-form-grid')
    for (const type of ['Http', 'Kafka', 'Nats', 'RabbitMq', 'Sqs']) {
      await types.locator(`input[value="${type}"]`).check()
      const layout = await grid.evaluate(element => {
        const bounds = element.getBoundingClientRect()
        return {
          display: getComputedStyle(element).display,
          columns: getComputedStyle(element).gridTemplateColumns.split(' ').length,
          width: bounds.width,
          fields: [...element.querySelectorAll('.destination-form-field')].map(field => ({
            wide: field.classList.contains('configuration-form-wide'),
            width: field.getBoundingClientRect().width,
            controls: [...field.querySelectorAll('.rt-TextFieldRoot, .rt-SelectTrigger, .rt-TextAreaRoot')].map(control => control.getBoundingClientRect().width),
          })),
        }
      })
      assert.equal(layout.display, 'grid', type)
      assert.equal(layout.columns, 2, type)
      assert.ok(layout.width > 700, `${type} uses the available editor width`)
      for (const field of layout.fields) {
        if (field.wide) assert.ok(Math.abs(field.width - layout.width) < 2, `${type} wide field spans both columns`)
        for (const width of field.controls) assert.ok(Math.abs(width - field.width) < 2, `${type} control fills its field`)
      }
    }
    await types.locator('input[value="Kafka"]').check()
    await page.setViewportSize({ width: 390, height: 844 })
    assert.equal(await grid.evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length), 1)
    assert.equal(await grid.evaluate(element => element.scrollWidth <= element.clientWidth + 1), true)
    const topic = grid.locator('.destination-form-field').filter({ has: page.getByText('Topic', { exact: true }) })
    const client = grid.locator('.destination-form-field').filter({ has: page.getByText('Client ID', { exact: true }) })
    assert.ok((await client.boundingBox()).y > (await topic.boundingBox()).y)
  } finally { await browser.close() }
})

const initial = {
  deliveryDefaults: { requestTimeoutSeconds: 45, maxAttempts: 8, batchLimit: 250 },
  retryDefaults: { initialDelaySeconds: 15, maxDelaySeconds: 1800, backoffMultiplier: 1.5 },
  retention: { eventRetentionDays: 60, deliveryAuditRetentionDays: 180, operationAuditRetentionDays: 180 },
  security: { requireHttpsDestinations: true, allowPrivateNetworkDestinations: false, requireDestinationCredentialEncryption: true },
  limits: { maxEventPayloadBytes: 262144, maxEventMetadataBytes: 65536, maxContractsPerCustomer: 300, maxConcurrentReplayJobs: 3 },
  updatedAt: '2026-04-01T00:00:00Z', updatedBy: 'operator',
}

test('reference-style settings preserve all fields, save/cancel and responsive grids', async () => {
  const browser = await chromium.launch({ headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
    let settings = structuredClone(initial)
    let submitted
    await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
    await page.route('https://localhost:7123/**', async route => {
      const path = new URL(route.request().url()).pathname
      let body = { items: [], page: 1, pageSize: 20, totalCount: 0, hasNext: false }
      if (path === '/v1/admin/users/me') body = { subject: 'configuration-operator', roles: ['Admin'], permissions: ['settings.write'] }
      if (path === '/v1/admin/settings/instance') {
        if (route.request().method() === 'PUT') {
          submitted = route.request().postDataJSON()
          settings = { ...submitted, updatedAt: initial.updatedAt, updatedBy: initial.updatedBy }
        }
        body = settings
      }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${appUrl}/settings`)
    const delivery = page.getByRole('region', { name: 'Delivery defaults', exact: true })
    await delivery.waitFor()
    assert.equal(await page.locator('.configuration-section input[type="number"]').count(), 13)
    assert.equal(await page.locator('.configuration-checkboxes input[type="checkbox"]').count(), 3)
    assert.equal(await page.locator('.configuration-help').count(), 13)
    const timeout = delivery.locator('input').first()
    assert.equal(await timeout.inputValue(), '45')
    const inputs = await delivery.locator('input').evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top))
    assert.ok(inputs.every(top => Math.abs(top - inputs[0]) < 2))
    const save = page.getByRole('button', { name: 'Save instance settings', exact: true })
    const instanceCard = page.locator('.configuration-editor').filter({ has: save })
    await timeout.fill('70')
    await instanceCard.getByRole('button', { name: 'Cancel', exact: true }).click()
    assert.equal(await timeout.inputValue(), '45')
    await timeout.fill('70')
    await save.click()
    await page.getByText('Instance settings saved.', { exact: true }).waitFor()
    assert.deepEqual(submitted, {
      deliveryDefaults: { ...initial.deliveryDefaults, requestTimeoutSeconds: 70 },
      retryDefaults: initial.retryDefaults, retention: initial.retention,
      security: initial.security, limits: initial.limits,
    })
    await page.setViewportSize({ width: 390, height: 844 })
    const mobileInputs = await delivery.locator('input').evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top))
    assert.ok(mobileInputs[1] > mobileInputs[0] && mobileInputs[2] > mobileInputs[1])
    assert.equal(await page.locator('.configuration-section').evaluateAll(elements => elements.every(element => element.scrollWidth <= element.clientWidth + 1)), true)
    await page.evaluate(() => document.querySelector('.configuration-help').focus())
    await page.getByRole('tooltip').waitFor()
    assert.match(await page.getByRole('tooltip').innerText(), /destination response/)
  } finally { await browser.close() }
})
