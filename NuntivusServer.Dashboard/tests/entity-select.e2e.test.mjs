import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'
const contractId = '7521772e-cc7a-4812-8eb2-b508e0b0fb33'
const eventId = '63f6cdd6-79cc-4c08-9892-e2c70c76c356'
const deliveryId = '40d29c18-877a-4a11-a35f-712abdc6f5b0'
const sourceId = 'c1b526a6-9fd3-468e-afb8-3022a82e1c4b'
const permissions = ['customers.read', 'contracts.read', 'contracts.write', 'events.read', 'deliveries.read', 'delivery-failures.read', 'audit-logs.read']
const contracts = [
  ...Array.from({ length: 26 }, (_, index) => ({ id: `contract-${index}`, name: `Common contract ${index}` })),
  { id: contractId, name: 'Special contract' },
]
const entities = {
  '/v1/customers': [{ id: 'customer-1', name: 'Acme customer' }],
  '/v1/admin/contracts': contracts,
  '/v1/events': [{ id: eventId, type: 'orders.created' }],
  '/v1/deliveries': [{ id: deliveryId, eventType: 'orders.created' }],
  '/v1/sources': [{ id: sourceId, name: 'Orders source' }],
}

function paged(items, page = 1, pageSize = 25) {
  return { items: items.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: items.length,
    totalPages: Math.ceil(items.length / pageSize), hasPrevious: page > 1, hasNext: page * pageSize < items.length }
}

async function openApp(granted = permissions) {
  const browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  const requests = []
  const state = { failContracts: false }
  await page.addInitScript(() => window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token'))
  await page.route('https://localhost:7123/**', async route => {
    const url = new URL(route.request().url())
    requests.push(url)
    let body = paged([])
    if (url.pathname === '/v1/admin/users/me') {
      body = { subject: 'selector-admin', roles: ['Admin'], permissions: granted }
    } else if (url.pathname === `/v1/contracts/${contractId}`) {
      body = { id: contractId, name: 'Special contract' }
    } else if (url.pathname === '/v1/admin/contracts' && state.failContracts) {
      await route.fulfill({ status: 403, contentType: 'application/json', body: '{"title":"Lookup unavailable"}' })
      return
    } else if (entities[url.pathname]) {
      const items = entities[url.pathname]
      if (url.searchParams.get('pageSize') === '25' || url.pathname === '/v1/sources') {
        const search = (url.searchParams.get('search') ?? '').toLowerCase()
        body = paged(items.filter(item => `${item.id} ${item.name ?? item.type ?? item.eventType}`.toLowerCase().includes(search)),
          Number(url.searchParams.get('page') ?? 1), Number(url.searchParams.get('pageSize') ?? 25))
      }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return { browser, page, requests, state }
}

for (const [path, labels] of [
  ['/deliveries', ['Event ID', 'Customer ID', 'Contract ID']],
  ['/deliveries/failures', ['Customer ID', 'Contract ID']],
  ['/events', ['Event ID', 'Source']],
  ['/contracts', ['Customer ID']],
  ['/audit-logs', ['Customer ID', 'Contract ID', 'Delivery ID', 'Event ID']],
  ['/alerts', ['Contract ID']],
]) {
  test(`${path} uses searchable selects for every existing entity ID`, async () => {
    const { browser, page } = await openApp()
    try {
      await page.goto(`${appUrl}${path}`)
      for (const label of labels) {
        await page.getByRole('combobox', { name: label, exact: true }).waitFor()
        assert.equal(await page.getByRole('textbox', { name: label, exact: true }).count(), 0)
        await page.getByRole('combobox', { name: label, exact: true }).click()
        await page.getByRole('combobox', { name: `Search ${label}`, exact: true }).waitFor()
        await page.getByRole('option').first().waitFor()
        await page.keyboard.press('Escape')
      }
    } finally { await browser.close() }
  })
}

test('contract select searches across pages, selects by keyboard, and clears the applied filter', async () => {
  const { browser, page, requests } = await openApp()
  try {
    await page.goto(`${appUrl}/deliveries`)
    await page.getByRole('combobox', { name: 'Contract ID', exact: true }).click()
    const search = page.getByRole('combobox', { name: 'Search Contract ID', exact: true })
    await search.fill('special')
    await page.getByRole('option', { name: /Special contract/ }).waitFor()
    await search.press('Enter')
    assert.equal(new URL(page.url()).searchParams.has('contractId'), false)
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click()
    await page.waitForURL(url => url.searchParams.get('contractId') === contractId)
    assert.ok(requests.some(url => url.pathname === '/v1/admin/contracts' && url.searchParams.get('search') === 'special'))
    await page.getByRole('combobox', { name: 'Contract ID', exact: true }).click()
    await page.getByRole('button', { name: 'Clear Contract ID', exact: true }).click()
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click()
    await page.waitForURL(url => !url.searchParams.has('contractId'))
  } finally { await browser.close() }
})

test('contract select loads additional pages and resolves selected IDs outside the initial page', async () => {
  const { browser, page } = await openApp()
  try {
    await page.goto(`${appUrl}/deliveries?contractId=${contractId}`)
    const select = page.getByRole('combobox', { name: 'Contract ID', exact: true })
    await select.getByText('Special contract').waitFor()
    await select.click()
    assert.equal(await page.getByRole('option', { name: /Special contract/ }).count(), 0)
    await page.getByRole('button', { name: 'Load more', exact: true }).click()
    await page.getByRole('option', { name: /Special contract/ }).waitFor()
    await page.getByRole('option', { name: /Special contract/ }).click()
  } finally { await browser.close() }
})

test('event and source selections submit entity IDs instead of labels', async () => {
  const { browser, page, requests } = await openApp()
  try {
    await page.goto(`${appUrl}/events`)
    for (const [label, option] of [['Event ID', /orders.created/], ['Source', /Orders source/]]) {
      await page.getByRole('combobox', { name: label, exact: true }).click()
      await page.getByRole('option', { name: option }).click()
    }
    const filtered = page.waitForResponse(response => { const url = new URL(response.url()); return url.pathname === '/v1/events' && url.searchParams.get('id') === eventId })
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click()
    await page.waitForURL(url => url.searchParams.get('id') === eventId && url.searchParams.get('source') === sourceId)
    await filtered
    assert.ok(requests.some(url => url.pathname === '/v1/events' && url.searchParams.get('id') === eventId && url.searchParams.get('source') === sourceId))
  } finally { await browser.close() }
})

test('selectors show no-match feedback and can recover from lookup failures', async () => {
  const { browser, page, state } = await openApp()
  try {
    await page.goto(`${appUrl}/deliveries`)
    await page.getByRole('combobox', { name: 'Contract ID', exact: true }).click()
    await page.getByRole('combobox', { name: 'Search Contract ID', exact: true }).fill('not-an-existing-entity')
    await page.getByText('No matches found.', { exact: true }).waitFor()
    assert.equal(await page.getByRole('option').count(), 0)
    state.failContracts = true
    await page.getByRole('combobox', { name: 'Search Contract ID', exact: true }).fill('special')
    await page.getByRole('button', { name: 'Retry', exact: true }).waitFor()
    state.failContracts = false
    await page.getByRole('button', { name: 'Retry', exact: true }).click()
    await page.getByRole('option', { name: /Special contract/ }).waitFor()
  } finally { await browser.close() }
})

test('selectors do not fetch entities without read permission and retain linked values', async () => {
  const { browser, page, requests } = await openApp(['deliveries.read'])
  try {
    await page.goto(`${appUrl}/deliveries?contractId=${contractId}`)
    const select = page.getByRole('combobox', { name: 'Contract ID', exact: true })
    await select.getByText(contractId, { exact: true }).waitFor()
    await select.click()
    await page.getByText('You do not have permission to browse these options.', { exact: true }).waitFor()
    assert.equal(requests.some(url => url.pathname.includes('contracts')), false)
    await page.getByRole('button', { name: 'Clear Contract ID', exact: true }).click()
  } finally { await browser.close() }
})
