import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

async function openPage(permissions, handler) {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()
    await page.addInitScript(() => {
        window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
    })
    await page.route('https://localhost:7123/**', async (route) => {
        const url = new URL(route.request().url())
        if (url.pathname === '/v1/admin/users/me') {
            await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
                subject: 'admin', displayName: 'Admin', roles: ['Admin'], permissions,
            }) })
            return
        }
        if (await handler(route, url)) return
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
            items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        }) })
    })
    return { browser, page }
}

test('invalid delivery event IDs stay on the filter and never call the API', async () => {
    let requests = 0
    const { browser, page } = await openPage(['deliveries.read'], async (route, url) => {
        if (url.pathname === '/v1/deliveries') requests++
        return false
    })
    try {
        await page.goto(`${appUrl}/deliveries`)
        await page.getByRole('textbox', { name: 'Event ID' }).waitFor()
        const beforeSubmit = requests
        await page.getByRole('textbox', { name: 'Event ID' }).fill('not-a-guid')
        await page.getByRole('button', { name: 'Apply filters' }).click()
        assert.equal(await page.getByRole('alert').first().textContent(), 'Event ID must be a valid GUID.')
        assert.equal(new URL(page.url()).searchParams.has('eventId'), false)
        assert.equal(requests, beforeSubmit)
        assert.equal(await page.locator('.global-error').count(), 0)
    } finally { await browser.close() }
})

test('deleting a user navigates to the list without refetching deleted detail', async () => {
    let deleted = false
    let deletedDetailRequests = 0
    const { browser, page } = await openPage(['users.read', 'roles.manage'], async (route, url) => {
        if (url.pathname === '/v1/admin/users/audit-user') {
            if (route.request().method() === 'DELETE') {
                deleted = true
                await route.fulfill({ status: 204 })
            } else {
                if (deleted) deletedDetailRequests++
                await route.fulfill({ status: deleted ? 404 : 200, contentType: 'application/json', body: JSON.stringify({
                    subject: 'audit-user', displayName: 'Audit User', roles: ['Admin'], createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
                }) })
            }
            return true
        }
        if (url.pathname === '/v1/admin/roles') {
            await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
            return true
        }
        return false
    })
    try {
        await page.goto(`${appUrl}/users/audit-user`)
        await page.getByRole('button', { name: 'Delete user' }).click()
        await page.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click()
        await page.waitForURL('**/users')
        assert.equal(deleted, true)
        assert.equal(deletedDetailRequests, 0)
        assert.equal(await page.locator('.global-error').count(), 0)
    } finally { await browser.close() }
})

test('query 404 stays contextual and stale global errors clear on navigation and recovery', async () => {
    const { browser, page } = await openPage(['users.read'], async (route, url) => {
        if (url.pathname === '/v1/admin/users/missing') {
            await route.fulfill({ status: 404, contentType: 'application/json', body: '{"title":"Not found"}' })
            return true
        }
        return false
    })
    try {
        await page.goto(`${appUrl}/users/missing`)
        await page.getByText('User not found.').waitFor()
        assert.equal(await page.locator('.global-error').count(), 0)
        await page.evaluate(() => window.dispatchEvent(new CustomEvent('nuntivus:api-error', {
            detail: { message: 'Temporary failure', route: location.pathname + location.search, request: 'POST /v1/test' },
        })))
        await page.getByText('Temporary failure').waitFor()
        await page.goto(`${appUrl}/forbidden`)
        assert.equal(await page.locator('.global-error').count(), 0)
        await page.evaluate(() => window.dispatchEvent(new CustomEvent('nuntivus:api-error', {
            detail: { message: 'Recovered failure', route: location.pathname + location.search, request: 'POST /v1/test' },
        })))
        await page.getByText('Recovered failure').waitFor()
        await page.evaluate(() => window.dispatchEvent(new CustomEvent('nuntivus:api-recovered', {
            detail: { route: location.pathname + location.search, request: 'POST /v1/test' },
        })))
        assert.equal(await page.locator('.global-error').count(), 0)
    } finally { await browser.close() }
})
