import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { createServer } from 'node:net'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'

const cwd = fileURLToPath(new URL('../../', import.meta.url))
const project = `nuntivus-quickstart-test-${randomBytes(5).toString('hex')}`
const otherProject = `${project}-other`
const receiver = `${project}-receiver`
async function freePort() {
  const server = createServer()
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  const port = server.address().port
  await new Promise(resolve => server.close(resolve))
  return port
}
const apiPort = await freePort()
const dashboardPort = await freePort()
const env = { ...process.env, NUNTIVUS_API_PORT: String(apiPort), NUNTIVUS_DASHBOARD_PORT: String(dashboardPort) }
function docker(args, allowFailure = false) {
  const result = spawnSync('docker', args, { cwd, env, encoding: 'utf8', timeout: 600_000 })
  if (!allowFailure && (result.error || result.status !== 0)) {
    throw new Error(`Docker ${args.slice(0, 2).join(' ')} failed (exit ${result.status}); inspect the isolated test stack.`)
  }
  return result.stdout?.trim() ?? ''
}
function compose(args, selectedProject = project, allowFailure = false) {
  return docker(['compose', '-p', selectedProject, '-f', 'docker-compose.quickstart.yml', ...args], allowFailure)
}
function query(sql) {
  return compose(['exec', '-T', 'postgres', 'psql', '-U', 'nuntivus', '-d', 'nuntivus', '-Atc', sql])
}
async function request(path, body, token) {
  const response = await fetch(`http://127.0.0.1:${apiPort}${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15_000),
  })
  assert.ok(response.ok, `API ${path} returned ${response.status}`)
  return response.status === 204 ? undefined : response.json()
}
const readSecret = path => compose(['exec', '-T', 'api', 'cat', path])

try {
  compose(['pull', '--quiet'])
  compose(['up', '-d', '--wait', '--wait-timeout', '240'])
  const password = readSecret('/run/nuntivus-api/admin-password')
  const signingKey = readSecret('/run/nuntivus-api/signing-key')
  const encryptionKey = readSecret('/run/nuntivus/encryption-key')
  for (const secret of [password, signingKey, encryptionKey]) assert.equal(Buffer.from(secret, 'base64').length, 32)
  assert.equal(new Set([password, signingKey, encryptionKey]).size, 3)
  const login = await request('/v1/auth/local/login', { username: 'admin', password })
  const token = login.accessToken
  assert.ok(token)
  assert.ok((await request('/v1/auth/me', undefined, token)).roles.includes('Admin'))
  assert.equal(query('SELECT COUNT(*) FROM admin_users;'), '1')
  assert.equal(query("SELECT COUNT(*) FROM audit_logs WHERE action = 'initial-admin-created';"), '1')
  const anonymous = await fetch(`http://127.0.0.1:${apiPort}/v1/customers`)
  assert.equal(anonymous.status, 401)
  const cors = await fetch(`http://127.0.0.1:${apiPort}/v1/auth/local/login`, {
    method: 'OPTIONS', headers: { Origin: `http://localhost:${dashboardPort}`, 'Access-Control-Request-Method': 'POST' },
  })
  assert.equal(cors.headers.get('access-control-allow-origin'), `http://localhost:${dashboardPort}`)
  const settingsResponse = await fetch(`http://127.0.0.1:${dashboardPort}/runtime-config.js`)
  assert.equal(settingsResponse.headers.get('cache-control'), 'no-store')
  const sandbox = { window: {} }
  runInNewContext(await settingsResponse.text(), sandbox)
  assert.equal(sandbox.window.__NUNTIVUS_CONFIG__.apiBaseUrl, `http://localhost:${apiPort}`)
  const page = await fetch(`http://127.0.0.1:${dashboardPort}/contracts/example`)
  assert.ok((await page.text()).includes('/runtime-config.js'))
  compose(['exec', '-T', 'worker', 'sh', '-ec', 'test ! -e /run/nuntivus-api/signing-key'])
  const logs = compose(['logs', '--no-color'])
  for (const secret of [password, signingKey, encryptionKey]) assert.ok(!logs.includes(secret), 'Startup logged a secret.')

  docker(['run', '-d', '--name', receiver, '--network', `${project}_default`, '--network-alias', 'receiver', 'nginx:alpine'])
  docker(['exec', receiver, 'sh', '-ec', 'printf \'server { listen 80; location / { return 200 "accepted"; } }\' > /etc/nginx/conf.d/default.conf; nginx -s reload'])
  await request('/v1/customers', { id: 'quickstart-smoke', name: 'Quick-start smoke' }, token)
  const sourceToken = randomBytes(24).toString('base64url')
  await request('/v1/admin/secrets', { name: 'secret.source.quickstart', value: sourceToken }, token)
  const source = await request('/v1/sources', {
    name: 'Quick-start source', type: 'HTTP_API',
    configuration: { authentication: { type: 'token', secretRef: 'secret.source.quickstart' } },
  }, token)
  const destination = await request('/v1/destinations', {
    name: 'Quick-start receiver', configuration: { type: 'http', url: 'http://receiver/', authentication: { type: 'none' } },
  }, token)
  const contract = await request('/v1/contracts', {
    customerId: 'quickstart-smoke', name: 'Quick-start contract', eventTypes: ['quickstart.created'],
    sourceIds: [source.id], destinationIds: [destination.id],
  }, token)
  await request(`/v1/contracts/${contract.id}/enable`, {}, token)
  const published = await fetch(`http://127.0.0.1:${apiPort}/v1/sources/${source.id}/events`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Nuntivus-Source-Token': sourceToken },
    body: JSON.stringify({ idempotencyKey: 'quickstart-event', type: 'quickstart.created', payload: { verified: true } }),
  })
  assert.ok(published.ok, `Ingestion returned ${published.status}`)
  const event = await published.json()
  assert.match(event.id, /^[0-9a-f-]{36}$/i)
  let delivered = false
  for (let attempt = 0; attempt < 45; attempt++) {
    if (query(`SELECT COUNT(*) FROM deliveries WHERE event_id = '${event.id}'::uuid AND status = 'Delivered';`) === '1') {
      delivered = true
      break
    }
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  assert.ok(delivered, 'Worker failed to deliver the test event.')

  docker(['rm', '-f', receiver])
  compose(['down'])
  compose(['up', '-d', '--wait', '--wait-timeout', '240'])
  assert.equal(readSecret('/run/nuntivus-api/admin-password'), password)
  assert.equal(readSecret('/run/nuntivus-api/signing-key'), signingKey)
  assert.equal(readSecret('/run/nuntivus/encryption-key'), encryptionKey)
  assert.ok((await request('/v1/auth/local/login', { username: 'admin', password })).accessToken)
  assert.ok((await request('/v1/auth/me', undefined, token)).roles.includes('Admin'))
  assert.equal(query('SELECT COUNT(*) FROM admin_users;'), '1')
  assert.equal(query("SELECT COUNT(*) FROM audit_logs WHERE action = 'initial-admin-created';"), '1')
  assert.equal(query("SELECT COUNT(*) FROM customers WHERE id = 'quickstart-smoke';"), '1')
  const replay = await fetch(`http://127.0.0.1:${apiPort}/v1/sources/${source.id}/events`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Nuntivus-Source-Token': sourceToken },
    body: JSON.stringify({ idempotencyKey: 'quickstart-event', type: 'quickstart.created', payload: { verified: true } }),
  })
  assert.ok(replay.ok, 'Encrypted source credential did not survive restart.')
  assert.equal((await replay.json()).id, event.id)

  compose(['up', '-d', 'initialize'], otherProject)
  const otherPassword = compose(['run', '--rm', '--no-deps', '--entrypoint', 'cat', 'initialize', '/api/admin-password'], otherProject)
  assert.notEqual(otherPassword, password, 'Independent installs reused a password.')
  console.log('PASS: pulled images, generated isolated secrets/admin, login/CORS, Dashboard settings/SPA, Worker delivery, encrypted data and restart persistence.')
} finally {
  docker(['rm', '-f', receiver], true)
  compose(['down', '--volumes', '--remove-orphans'], project, true)
  compose(['down', '--volumes', '--remove-orphans'], otherProject, true)
}
