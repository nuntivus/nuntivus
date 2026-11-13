import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const cases = [
  ['Http', 1, { 'Destination URL': 'https://example.com/events' }, { url: 'https://example.com/events' }],
  ['Kafka', 2, { 'Bootstrap servers': 'broker:9093', Topic: 'events' }, { bootstrapServers: 'broker:9093', topic: 'events' }],
  ['Nats', 3, { 'NATS URL': 'tls://broker.example.com:4222', Subject: 'events' }, { url: 'tls://broker.example.com:4222', subject: 'events' }],
  ['RabbitMq', 4, { URI: 'amqps://broker.example.com', Exchange: 'events', 'Routing key': 'events.created' }, { uri: 'amqps://broker.example.com', exchange: 'events', routingKey: 'events.created' }],
  ['Sqs', 5, { Region: 'us-east-1', 'Queue URL': 'https://sqs.us-east-1.amazonaws.com/123/events' }, { region: 'us-east-1', queueUrl: 'https://sqs.us-east-1.amazonaws.com/123/events' }],
]
function field(page, label) {
  if (label === 'Destination URL') return page.getByRole('textbox', { name: label, exact: true })
  return page.locator('.destination-form-field').filter({ has: page.getByText(label, { exact: true }) }).getByRole('textbox')
}
for (const [type, numericType, fields, expected] of cases) {
  test(`${type} Destination creates a draft, saves configuration, hydrates and confirms test requests`, async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()
    const calls = []
    let destination
    try {
      await page.addInitScript(() => sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const path = new URL(route.request().url()).pathname
        const method = route.request().method()
        let body = {}
        if (path === '/v1/admin/users/me') body = { subject: 'test-admin', roles: ['Admin'], permissions: ['contracts.read', 'contracts.write'] }
        else if (path === '/v1/admin/secrets') body = []
        else if (path.endsWith('/test-connection')) { calls.push({ path, input: route.request().postDataJSON() }); body = { message: `${type} probe passed` } }
        else if (path === '/v1/destinations' && method === 'GET') body = { items: destination ? [destination] : [], page: 1, totalCount: destination ? 1 : 0, hasNext: false }
        else if (path === '/v1/destinations' && method === 'POST') {
          const input = route.request().postDataJSON()
          calls.push({ path, input })
          destination = { ...input, id: 'destination-1', configured: false, configuration: '{}', enabled: false,
            createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }
          body = destination
        } else if (path === '/v1/destinations/destination-1') {
          if (method === 'PUT') { const input = route.request().postDataJSON(); calls.push({ path, input }); destination = { ...destination, name: input.name, configured: true, configuration: JSON.stringify(input.configuration), updatedAt: '2026-01-02T00:00:00Z' } }
          body = destination
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/destinations`)
      await page.getByRole('button', { name: 'Add Destination', exact: true }).click()
      await page.getByRole('textbox', { name: 'Destination name' }).fill(`${type} destination`)
      await page.getByRole('combobox', { name: 'Destination type' }).click()
      assert.deepEqual(await page.getByRole('option').allTextContents(), cases.map(entry => entry[0]))
      await page.getByRole('option', { name: type, exact: true }).click()
      await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click()
      await page.waitForURL(/\/destinations\/destination-1\?edit=1$/)
      assert.equal(await page.getByRole('alertdialog').count(), 0)
      assert.equal(calls[0].input.type, numericType)
      assert.equal(calls[0].input.configuration, undefined)
      await page.getByText('Advanced Settings', { exact: true }).click()
      for (const [label, value] of Object.entries(fields)) await field(page, label).fill(value)
      await page.getByRole('button', { name: 'Save configuration', exact: true }).click()
      await page.waitForURL(/\/destinations\/destination-1$/)
      for (const [key, value] of Object.entries(expected)) assert.equal(calls[1].input.configuration[key], value)
      assert.equal(calls[1].input.configuration.type.toLowerCase(), type.toLowerCase())
      await page.getByRole('button', { name: 'Save changes' }).waitFor()
      await page.getByText('Advanced Settings', { exact: true }).click()
      for (const [label, value] of Object.entries(fields)) assert.equal(await field(page, label).inputValue(), value)
      await page.getByRole('textbox', { name: 'JSON test payload' }).fill('{"probe":true}')
      await page.getByRole('button', { name: 'Test connection', exact: true }).click()
      const confirm = page.getByRole('alertdialog')
      await confirm.getByRole('button', { name: 'Cancel', exact: true }).click()
      assert.equal(calls.length, 2)
      await page.getByRole('button', { name: 'Test connection', exact: true }).click()
      await confirm.getByRole('button', { name: /Send test request|Confirm/, exact: true }).click()
      await page.getByRole('status').filter({ hasText: `${type} probe passed` }).waitFor()
      assert.equal(calls[2].path, '/v1/destinations/destination-1/test-connection')
      assert.deepEqual(calls[2].input.payload, { probe: true })
      for (const [key, value] of Object.entries(expected)) assert.equal(calls[2].input.configuration[key], value)
    } finally { await browser.close() }
  })
}
