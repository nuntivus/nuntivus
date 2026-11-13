import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const cases = [
  ['HTTP_API', {}, { authentication: { type: 'none' }, method: 'POST' }],
  ['RABBITMQ', { URI: 'amqps://broker.example.com', Queue: 'events' }, { uri: 'amqps://broker.example.com', queue: 'events' }],
  ['KAFKA', { 'Bootstrap Servers': 'broker:9093', Topic: 'events', 'Group Id': 'ingestion' }, { bootstrapServers: 'broker:9093', topic: 'events', groupId: 'ingestion' }],
  ['SQS', { Region: 'us-east-1', 'Queue URL': 'https://sqs.us-east-1.amazonaws.com/123/events' }, { region: 'us-east-1', queueUrl: 'https://sqs.us-east-1.amazonaws.com/123/events' }],
  ['NATS', { URL: 'tls://broker.example.com:4222', Stream: 'EXTERNAL', Subject: 'events', Durable: 'ingestion' }, { url: 'tls://broker.example.com:4222', stream: 'EXTERNAL', subject: 'events', durable: 'ingestion' }],
  ['HTTP_POLLING', { URL: 'https://example.com/events', 'Polling interval (seconds)': '30', 'Plain headers (JSON)': '{"X-Client":"test"}' }, { url: 'https://example.com/events', intervalSeconds: 30, headers: { 'X-Client': 'test' } }],
  ['FOLDER', { Path: '/var/events', 'Polling interval (seconds)': '30' }, { path: '/var/events', intervalSeconds: 30 }],
  ['S3', { Region: 'us-east-1', Bucket: 'events', Prefix: 'incoming/', 'Polling interval (seconds)': '30' }, { region: 'us-east-1', bucket: 'events', prefix: 'incoming/', intervalSeconds: 30 }],
]
for (const [type, fields, configuration] of cases) {
  test(`${type} Source creates, tests, hydrates and updates its own configuration`, async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()
    const calls = []
    let source
    try {
      await page.addInitScript(() => sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
      await page.route('https://localhost:7123/**', async route => {
        const path = new URL(route.request().url()).pathname
        const method = route.request().method()
        let body = {}
        if (path === '/v1/admin/users/me') body = { subject: 'test-admin', roles: ['Admin'], permissions: ['contracts.read', 'contracts.write'] }
        else if (path === '/v1/admin/secrets') body = []
        else if (path.endsWith('/test-connection')) { calls.push({ path, input: route.request().postDataJSON() }); body = { message: `${type} probe passed` } }
        else if (path === '/v1/sources' && method === 'POST') {
          const input = route.request().postDataJSON()
          calls.push({ path, input })
          source = { ...input, id: 'source-1', enabled: true, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }
          body = source
        } else if (path === '/v1/sources/source-1') {
          if (method === 'PUT') { const input = route.request().postDataJSON(); calls.push({ path, input }); source = { ...source, ...input, updatedAt: '2026-01-02T00:00:00Z' } }
          body = source
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
      })
      await page.goto(`${appUrl}/sources/new`)
      await page.getByRole('combobox', { name: 'Source type' }).click()
      const options = await page.getByRole('option').allTextContents()
      assert.deepEqual(options, cases.map(entry => entry[0]))
      await page.getByRole('option', { name: type, exact: true }).click()
      await page.getByRole('textbox', { name: /Name/ }).fill(`${type} source`)
      for (const [label, value] of Object.entries(fields)) await page.getByRole('textbox', { name: label }).fill(value)
      await page.getByRole('button', { name: 'Test connection', exact: true }).click()
      await page.getByRole('status').filter({ hasText: `${type} probe passed` }).waitFor()
      assert.deepEqual(calls[0].input.configuration, configuration)
      assert.equal(calls[0].input.type, type)
      await page.getByRole('button', { name: 'Create source' }).click()
      await page.waitForURL(/\/sources\/source-1$/)
      assert.deepEqual(calls[1].input.configuration, configuration)
      assert.equal(calls[1].input.type, type)
      await page.getByRole('heading', { name: type === 'HTTP_API' ? 'HTTP Source Configuration' : `${type} Source Configuration`, exact: true }).waitFor()
      await page.getByRole('textbox', { name: /Name/ }).fill(`${type} updated`)
      await page.getByText('Advanced Settings', { exact: true }).click()
      for (const [label, value] of Object.entries(fields)) assert.equal(await page.getByRole('textbox', { name: label }).inputValue(), label.includes('JSON') ? JSON.stringify(JSON.parse(value), null, 2) : value)
      await page.getByRole('button', { name: 'Test connection', exact: true }).click()
      await page.getByRole('status').filter({ hasText: `${type} probe passed` }).waitFor()
      assert.equal(calls[2].path, '/v1/sources/source-1/test-connection')
      assert.deepEqual(calls[2].input.configuration, configuration)
      await page.getByRole('button', { name: 'Save changes' }).click()
      await page.getByRole('heading', { name: `${type} updated`, exact: true }).waitFor()
      assert.deepEqual(calls[3].input.configuration, configuration)
      assert.equal(calls[3].input.name, `${type} updated`)
    } finally { await browser.close() }
  })
}
