import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

for (const initialStatus of ['Failed', 3]) {
    test(`delivery detail refreshes background retry success from ${JSON.stringify(initialStatus)} without reloading`, async () => {
        const browser = await chromium.launch({ headless: true })
        const page = await browser.newPage()
        let completed = false
        let detailRequests = 0
        let successfulAttemptsRequests = 0
        let successfulFailureRequests = 0
        try {
            await page.addInitScript(() => {
                window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
            })
            await page.route('https://localhost:7123/**', async route => {
                const path = new URL(route.request().url()).pathname
                let body = {}
                if (path === '/v1/admin/users/me') {
                    body = { subject: 'playwright.admin', displayName: 'Playwright Admin', roles: ['Admin'],
                        permissions: ['deliveries.read', 'delivery-failures.read', 'events.read'] }
                } else if (path === '/v1/deliveries/test-delivery') {
                    detailRequests++
                    body = {
                        id: 'test-delivery', eventId: 'test-event', customerId: 'test-customer', contractId: 'test-contract',
                        destinationType: 'Http', eventType: 'test.created', status: completed ? 'Delivered' : initialStatus,
                        attemptCount: completed ? 2 : 1, lastError: completed ? null : 'Connection refused',
                        nextAttemptAt: completed ? null : '2026-01-01T00:02:00Z',
                        createdAt: '2026-01-01T00:00:00Z', updatedAt: completed ? '2026-01-01T00:03:00Z' : '2026-01-01T00:01:00Z',
                    }
                } else if (path === '/v1/deliveries/test-delivery/attempts') {
                    if (completed) successfulAttemptsRequests++
                    body = completed ? [{ id: 'successful-attempt', attemptNumber: 2, succeeded: true,
                        startedAt: '2026-01-01T00:03:00Z', completedAt: '2026-01-01T00:03:01Z', durationMs: 1000,
                        metadata: { httpStatus: '200' }, error: null, failureCategory: null }] : []
                } else if (path === '/v1/deliveries/test-delivery/failure') {
                    if (completed) successfulFailureRequests++
                    body = completed ? {} : { failureCategory: 'Transient' }
                }
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
            })
            await page.goto(`${appUrl}/deliveries/test-delivery`)
            await page.locator('.delivery-error-callout').waitFor()
            completed = true
            await page.locator('.delivery-success-callout').waitFor({ timeout: 10000 })
            await page.waitForFunction(() => document.querySelector('.delivery-status-track')?.textContent.includes('2 attempts'))
            assert.deepEqual(await page.locator('.delivery-status-dot').allTextContents(), ['✓', '✓', '✓', '✓'])
            assert.equal(await page.locator('.delivery-error-callout').count(), 0)
            assert.equal(await page.locator('.delivery-metric.delivery-tone-success').count(), 1)
            await page.getByText('Delivery attempts', { exact: true }).waitFor()
            await page.getByText('Success', { exact: true }).waitFor()
            assert.ok(successfulAttemptsRequests > 0)
            assert.ok(successfulFailureRequests > 0)
            const terminalRequestCount = detailRequests
            await page.waitForTimeout(3500)
            assert.equal(detailRequests, terminalRequestCount, 'Polling stops after Delivered')
        } finally {
            await browser.close()
        }
    })
}

for (const { status, attemptCount, expectedDots, expectedDetails } of [
    { status: 'Dead', attemptCount: 1, expectedDots: ['✓', '✓', '×', '4'], expectedDetails: ['1 attempt', 'Not delivered', 'Not completed'] },
    { status: 'Dead', attemptCount: 0, expectedDots: ['×', '2', '3', '4'], expectedDetails: ['Waiting', 'Not delivered', 'Not completed'] },
    { status: 'Failed', attemptCount: 1, expectedDots: ['✓', '✓', '×', '4'], expectedDetails: ['1 attempt', 'Not delivered', 'Pending'] },
    { status: 'Delivered', attemptCount: 1, expectedDots: ['✓', '✓', '✓', '✓'], expectedDetails: ['1 attempt', 'Done'] },
]) {
    test(`delivery timeline shows ${status} after ${attemptCount} attempts`, async () => {
        const browser = await chromium.launch({ headless: true })
        const page = await browser.newPage()

        try {
            await page.addInitScript(() => {
                window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
            })

            await page.route('https://localhost:7123/**', async (route) => {
                const path = new URL(route.request().url()).pathname
                let body = {}

                if (path === '/v1/admin/users/me') {
                    body = {
                        subject: 'playwright.admin',
                        displayName: 'Playwright Admin',
                        roles: ['Admin'],
                        permissions: ['deliveries.read', 'delivery-failures.read', 'events.read'],
                    }
                } else if (path === '/v1/deliveries/test-delivery') {
                    body = {
                        id: 'test-delivery', eventId: 'test-event', customerId: 'test-customer', contractId: 'test-contract',
                        destinationType: 'Http', eventType: 'test.created', status, attemptCount,
                        lastError: status === 'Dead' ? 'HTTP 405' : null, nextAttemptAt: null,
                        createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:01:00Z',
                    }
                } else if (path === '/v1/deliveries/test-delivery/attempts') {
                    body = []
                }

                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
            })

            await page.goto(`${appUrl}/deliveries/test-delivery`)
            const track = page.locator('.delivery-status-track')
            await track.waitFor()
            assert.deepEqual(await track.locator('.delivery-status-dot').allTextContents(), expectedDots)
            for (const detail of expectedDetails) {
                assert.equal(await track.getByText(detail, { exact: true }).count(), 1)
            }
        } finally {
            await browser.close()
        }
    })
}

for (const { action, endpoint, confirmText } of [
    { action: 'Retry', endpoint: 'retry', confirmText: 'Retry now' },
    { action: 'Redelivery (requeue)', endpoint: 'requeue', confirmText: 'Requeue' },
]) {
    test(`${action} validates the operation ID and rotates it after success`, async () => {
        const browser = await chromium.launch({ headless: true })
        const page = await browser.newPage()
        const requests = []

        try {
            await page.addInitScript(() => {
                window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
            })

            await page.route('https://localhost:7123/**', async (route) => {
                const path = new URL(route.request().url()).pathname
                let body = {}

                if (path === '/v1/admin/users/me') {
                    body = {
                        subject: 'playwright.admin', displayName: 'Playwright Admin', roles: ['Admin'],
                        permissions: ['deliveries.read', 'deliveries.operate', 'delivery-failures.read', 'events.read'],
                    }
                } else if (path === '/v1/deliveries/test-delivery') {
                    body = {
                        id: 'test-delivery', eventId: 'test-event', customerId: 'test-customer', contractId: 'test-contract',
                        destinationType: 'Http', eventType: 'test.created', status: 'Dead', attemptCount: 1,
                        lastError: 'HTTP 405', nextAttemptAt: null,
                        createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:01:00Z',
                    }
                } else if (path === '/v1/deliveries/test-delivery/attempts') {
                    body = []
                } else if (path === `/v1/deliveries/test-delivery/${endpoint}`) {
                    requests.push(JSON.parse(route.request().postData()))
                    body = { status: 'Pending' }
                }

                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
            })

            await page.goto(`${appUrl}/deliveries/test-delivery`)
            const operationId = page.getByPlaceholder('GUID')
            await operationId.waitFor()
            assert.match(await operationId.inputValue(), /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)

            await operationId.fill('test')
            assert.equal(await page.getByText('Operation ID must be a valid GUID.').count(), 1)
            assert.equal(await page.getByRole('button', { name: action, exact: true }).isDisabled(), true)
            assert.equal(requests.length, 0)

            const validId = '8f3d6e6c-8dd6-4abc-9f48-8a9522537273'
            await operationId.fill(validId)
            await page.getByRole('button', { name: action, exact: true }).click()
            await page.getByRole('button', { name: confirmText, exact: true }).click()
            await page.waitForFunction(() => document.querySelector('input[placeholder="GUID"]')?.value !== '8f3d6e6c-8dd6-4abc-9f48-8a9522537273')

            assert.equal(requests.length, 1)
            assert.equal(requests[0].operationId, validId)
            assert.match(await operationId.inputValue(), /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)
        } finally {
            await browser.close()
        }
    })
}
