import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

test('customer name field is accessible by its visible label', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            const json = (body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })

            if (url.pathname === '/v1/admin/users/me') {
                await json({
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: [
                        'customers.read',
                        'customers.write',
                        'contracts.read',
                        'deliveries.read',
                        'delivery-failures.read',
                    ],
                })
                return
            }

            if (url.pathname === '/v1/customers/regression-customer') {
                await json({
                    id: 'regression-customer',
                    name: 'Regression Customer',
                    enabled: true,
                    createdAt: '2026-01-01T00:00:00Z',
                    updatedAt: '2026-01-01T00:00:00Z',
                })
                return
            }

            if (url.pathname === '/v1/destination-health/customers/regression-customer') {
                await json([])
                return
            }

            await json({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
        })

        await page.goto(`${appUrl}/customers/regression-customer`)

        const nameInput = page.getByRole('textbox', { name: 'Name', exact: true })
        await nameInput.waitFor()
        assert.equal(await nameInput.inputValue(), 'Regression Customer')

        await nameInput.fill('')
        assert.equal(await nameInput.getAttribute('aria-invalid'), 'true')
        assert.equal(await page.getByRole('alert').textContent(), 'Name is required.')
        assert.equal(await page.getByRole('button', { name: 'Save customer' }).isDisabled(), true)
    } finally {
        await browser.close()
    }
})

test('contract basics fields are accessible by their visible labels', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            let body = { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }

            if (url.pathname === '/v1/admin/users/me') {
                body = {
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: [
                        'contracts.read',
                        'contracts.write',
                        'customers.read',
                        'event-definitions.read',
                    ],
                }
            } else if (url.pathname === '/v1/customers') {
                body.items = [{ id: 'customer-1', name: 'Customer One', enabled: true }]
                body.totalCount = 1
                body.totalPages = 1
            } else if (url.pathname === '/v1/event-definitions') {
                body.items = [{ eventType: 'order.created', displayName: 'Order created' }]
                body.totalCount = 1
                body.totalPages = 1
            }

            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify(body),
            })
        })

        await page.goto(`${appUrl}/contracts/new`)

        await page.getByRole('combobox', { name: 'Customer', exact: true }).waitFor()
        assert.equal(await page.getByRole('textbox', { name: 'Contract name (optional)', exact: true }).count(), 1)
        assert.equal(await page.getByRole('combobox', { name: 'Event type', exact: true }).count(), 1)

        await page.getByRole('button', { name: 'Next' }).click()
        assert.deepEqual(await page.getByRole('alert').allTextContents(), [
            'Customer is required.',
            'Event type is required.',
        ])

        await page.getByRole('combobox', { name: 'Customer', exact: true }).click()
        await page.getByRole('option', { name: 'Customer One (customer-1)' }).click()
        await page.getByRole('combobox', { name: 'Event type', exact: true }).click()
        await page.getByRole('option', { name: 'order.created' }).click()
        await page.getByRole('button', { name: 'Next' }).click()

        assert.equal(await page.getByRole('combobox', { name: 'Destination type', exact: true }).count(), 1)
        assert.equal(await page.getByRole('textbox', { name: 'Destination URL', exact: true }).count(), 1)
        assert.equal(await page.getByRole('combobox', { name: 'Authentication', exact: true }).count(), 1)

        await page.getByRole('button', { name: 'Next' }).click()
        assert.deepEqual(await page.getByRole('alert').allTextContents(), ['Destination URL is required.'])
    } finally {
        await browser.close()
    }
})

test('missing contract destination health does not show a global error notification', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            let status = 200
            let body

            if (url.pathname === '/v1/admin/users/me') {
                body = {
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: ['contracts.read', 'contracts.write', 'deliveries.read', 'delivery-failures.read'],
                }
            } else if (url.pathname === '/v1/contracts/contract-1') {
                body = {
                    id: 'contract-1',
                    customerId: 'customer-1',
                    enabled: true,
                    eventTypes: ['order.created'],
                    destinationType: 'Http',
                    destinationConfiguration: '{"url":"https://example.com","authenticationType":"none"}',
                    createdAt: '2026-01-01T00:00:00Z',
                    updatedAt: '2026-01-01T00:00:00Z',
                    name: 'Contract One',
                }
            } else if (url.pathname === '/v1/destination-health/contracts/contract-1') {
                status = 404
                body = { message: "Destination health for contract 'contract-1' was not found." }
            } else {
                body = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }
            }

            await route.fulfill({
                status,
                contentType: 'application/json',
                body: JSON.stringify(body),
            })
        })

        await page.goto(`${appUrl}/contracts/contract-1`)
        await page.getByRole('heading', { name: 'Destination health' }).waitFor()
        await page.getByText("Destination health for contract 'contract-1' was not found.").waitFor()
        await page.waitForTimeout(1_100)

        assert.equal(await page.getByRole('region', { name: 'Notifications' }).textContent(), '')
    } finally {
        await browser.close()
    }
})

test('numeric delivery statuses use their readable names', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()
    const delivery = {
        id: 'delivery-1',
        eventId: 'event-1',
        customerId: 'customer-1',
        contractId: 'contract-1',
        destinationType: 'Http',
        eventType: 'order.created',
        status: 4,
        attemptCount: 1,
        lastError: 'Delivery failed',
        nextAttemptAt: null,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:01Z',
        attemptSummary: { totalAttempts: 1, successfulAttempts: 0, failedAttempts: 1, lastAttemptAt: '2026-01-01T00:00:01Z' },
        latestFailure: { failureCategory: 'Permanent', error: 'Delivery failed', failedAt: '2026-01-01T00:00:01Z' },
    }

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            let body

            if (url.pathname === '/v1/admin/users/me') {
                body = {
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: ['deliveries.read', 'deliveries.operate'],
                }
            } else if (url.pathname === '/v1/deliveries/delivery-1') {
                body = delivery
            } else if (url.pathname === '/v1/deliveries/delivery-1/failure') {
                body = { deliveryId: 'delivery-1', status: 4, lastError: 'Delivery failed', failureCategory: 'Permanent', failedAt: '2026-01-01T00:00:01Z', nextAttemptAt: null, updatedAt: '2026-01-01T00:00:01Z' }
            } else if (url.pathname === '/v1/deliveries/delivery-1/attempts') {
                body = []
            } else {
                body = { items: [delivery], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }
            }

            await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
        })

        await page.goto(`${appUrl}/deliveries`)
        const row = page.getByRole('row', { name: /delivery-1/ })
        await row.waitFor()
        assert.match(await row.innerText(), /Dead/)

        await page.goto(`${appUrl}/deliveries/delivery-1`)
        await page.getByRole('heading', { name: 'Overview' }).waitFor()
        assert.equal(await page.getByText('Dead', { exact: true }).count(), 1)
    } finally {
        await browser.close()
    }
})

test('user display name field is accessible by its visible label', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            let body

            if (url.pathname === '/v1/admin/users/me') {
                body = {
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: ['users.read', 'users.manage-roles'],
                }
            } else if (url.pathname === '/v1/admin/users/user-1') {
                body = {
                    subject: 'user-1',
                    displayName: 'User One',
                    roles: ['Viewer'],
                    createdAt: '2026-01-01T00:00:00Z',
                    updatedAt: '2026-01-01T00:00:00Z',
                }
            } else {
                body = { roles: [{ name: 'Viewer', description: 'Read access', permissions: [] }] }
            }

            await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
        })

        await page.goto(`${appUrl}/users/user-1`)
        const displayName = page.getByRole('textbox', { name: 'Display name', exact: true })
        await displayName.waitFor()
        assert.equal(await displayName.inputValue(), 'User One')
    } finally {
        await browser.close()
    }
})

test('instance numeric settings have visible labels and cancel restores values', async () => {
    const browser = await chromium.launch({ headless: true })
    const page = await browser.newPage()

    try {
        await page.addInitScript(() => {
            window.sessionStorage.setItem('nuntivus.dashboard.auth.token', 'playwright-token')
        })

        await page.route('https://localhost:7123/**', async (route) => {
            const url = new URL(route.request().url())
            const body = url.pathname === '/v1/admin/users/me'
                ? {
                    subject: 'playwright.admin',
                    displayName: 'Playwright Admin',
                    roles: ['Admin'],
                    permissions: ['settings.read', 'settings.write'],
                }
                : {
                    deliveryDefaults: { requestTimeoutSeconds: 30, maxAttempts: 6, batchLimit: 500 },
                    retryDefaults: { initialDelaySeconds: 30, maxDelaySeconds: 3600, backoffMultiplier: 2 },
                    security: { requireHttpsDestinations: true, allowPrivateNetworkDestinations: false, requireDestinationCredentialEncryption: true },
                    retention: { eventRetentionDays: 90, deliveryAuditRetentionDays: 365, operationAuditRetentionDays: 365 },
                    limits: { maxEventPayloadBytes: 262144, maxEventMetadataBytes: 65536, maxContractsPerCustomer: 500, maxConcurrentReplayJobs: 5 },
                    updatedAt: '2026-01-01T00:00:00Z',
                    updatedBy: 'system',
                }

            await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
        })

        await page.goto(`${appUrl}/settings`)
        await page.getByRole('combobox', { name: 'Theme', exact: true }).waitFor()

        const expectedLabels = [
            'Request timeout (seconds)',
            'Maximum attempts',
            'Batch limit',
            'Initial delay (seconds)',
            'Maximum delay (seconds)',
            'Backoff multiplier',
            'Event retention (days)',
            'Delivery audit retention (days)',
            'Operation audit retention (days)',
            'Maximum event payload (bytes)',
            'Maximum event metadata (bytes)',
            'Maximum contracts per customer',
            'Maximum concurrent replay jobs',
        ]

        await page.getByRole('spinbutton', { name: expectedLabels[0], exact: true }).waitFor()

        for (const label of expectedLabels) {
            assert.equal(await page.getByRole('spinbutton', { name: label, exact: true }).count(), 1)
        }

        const timeout = page.getByRole('spinbutton', { name: 'Request timeout (seconds)', exact: true })
        await timeout.fill('31')
        assert.equal(await page.getByRole('button', { name: 'Save instance settings' }).isDisabled(), false)
        await page.locator('main').getByRole('button', { name: 'Cancel' }).last().click()
        assert.equal(await timeout.inputValue(), '30')
    } finally {
        await browser.close()
    }
})