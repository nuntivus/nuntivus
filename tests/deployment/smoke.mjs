import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'

const project = process.env.SMOKE_PROJECT ?? 'nuntivus-ci'
const compose = ['compose', '-p', project, '-f', 'docker-compose.yml', '-f', 'tests/deployment/compose.smoke.yml']
const receiver = `${project}-receiver`
const password = randomBytes(24).toString('base64url')
const sourceToken = randomBytes(24).toString('base64url')
const username = 'publication-smoke-admin'
function docker(args, input, allowFailure = false) {
  const result = spawnSync('docker', args, { input, encoding: 'utf8', timeout: 120_000 })
  if (result.error || (result.status !== 0 && !allowFailure))
    throw new Error(`Docker operation failed: ${args.slice(0, 3).join(' ')} (exit ${result.status}); inspect local container logs.`)
  return result
}
function request(path, body, token, sourceCredential, allowFailure = false) {
  const args = ['wget', '-qO-', '--header=Content-Type: application/json']
  if (allowFailure) args.push('-S')
  if (token) args.push(`--header=Authorization: Bearer ${token}`)
  if (sourceCredential) args.push(`--header=X-Nuntivus-Source-Token: ${sourceCredential}`)
  if (body !== undefined) args.push('--post-file=/tmp/publication-request.json')
  args.push(`http://127.0.0.1:8080${path}`)
  const command = body === undefined
    ? [...compose, 'exec', '-T', 'api', ...args]
    : [...compose, 'exec', '-T', 'api', 'sh', '-c',
      'umask 077; cat > /tmp/publication-request.json; trap "rm -f /tmp/publication-request.json" EXIT; "$@"', 'sh', ...args]
  const result = docker(command, body === undefined ? undefined : JSON.stringify(body), allowFailure)
  return allowFailure ? result : JSON.parse(result.stdout)
}
function assert(condition, message) { if (!condition) throw new Error(message) }
try {
  const bootstrap = [...compose, 'exec', '-T', '-e', `AdminBootstrap__Username=${username}`, 'api',
    'dotnet', 'NuntivusServer.Api.dll', '--create-admin']
  const first = docker(bootstrap, `${password}\n`)
  assert(!`${first.stdout}${first.stderr}`.includes(password), 'Bootstrap logged the password.')
  const second = docker(bootstrap, `${password}\n`, true)
  assert(second.status !== 0 && `${second.stdout}${second.stderr}`.includes('before any admin users exist'),
    'Second admin bootstrap was not refused.')
  assert(!`${second.stdout}${second.stderr}`.includes(password), 'Rejected bootstrap logged the password.')
  const token = request('/v1/auth/local/login', { username, password }).accessToken
  assert(typeof token === 'string' && token.length > 0, 'Local login failed.')
  const me = request('/v1/auth/me', undefined, token)
  assert(me.roles.includes('Admin'), 'Initial account lacks Admin role.')
  const unauthorized = request('/v1/customers', undefined, undefined, undefined, true)
  assert(unauthorized.status !== 0 && unauthorized.stderr.includes('401'), 'Administration allows anonymous access.')

  docker(['run', '-d', '--name', receiver, '--network', `${project}_default`, '--network-alias', 'receiver', 'nginx:alpine'])
  docker(['exec', receiver, 'sh', '-ec', 'printf \'server { listen 80; location / { return 200 "accepted"; } }\' > /etc/nginx/conf.d/default.conf; nginx -s reload'])
  request('/v1/customers', { id: 'publication-smoke', name: 'Publication smoke' }, token)
  request('/v1/admin/secrets', { name: 'secret.source.publication', value: sourceToken }, token)
  const source = request('/v1/sources', {
    name: 'Publication HTTP source', type: 'HTTP_API',
    configuration: { authentication: { type: 'token', secretRef: 'secret.source.publication' } }
  }, token)
  const destination = request('/v1/destinations', {
    name: 'Publication receiver', configuration: { type: 'http', url: 'http://receiver/', authentication: { type: 'none' } }
  }, token)
  const contract = request('/v1/contracts', {
    customerId: 'publication-smoke', name: 'Publication smoke', eventTypes: ['smoke.created'],
    sourceIds: [source.id], destinationIds: [destination.id]
  }, token)
  request(`/v1/contracts/${contract.id}/enable`, {}, token)
  const event = { idempotencyKey: 'publication-smoke-event', type: 'smoke.created', timestamp: new Date().toISOString(),
    payload: { verified: true }, metadata: { traceId: 'deployment-smoke' } }
  const denied = request(`/v1/sources/${source.id}/events`, event, undefined, undefined, true)
  assert(denied.status !== 0 && denied.stderr.includes('401'), 'Source accepts requests without its token.')
  const accepted = request(`/v1/sources/${source.id}/events`, event, undefined, sourceToken)
  const duplicate = request(`/v1/sources/${source.id}/events`, event, undefined, sourceToken)
  assert(accepted.id === duplicate.id, 'Duplicate ingestion produced another event.')
  let delivered = false
  for (let attempt = 0; attempt < 30; attempt++) {
    const count = docker([...compose, 'exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', 'nuntivus', '-Atc',
      `SELECT count(*) FROM deliveries WHERE event_id = '${accepted.id}'::uuid AND status = 'Delivered';`]).stdout.trim()
    if (count === '1') { delivered = true; break }
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  assert(delivered, 'Worker did not complete delivery to the controlled receiver.')
  const audit = docker([...compose, 'exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', 'nuntivus', '-Atc',
    "SELECT count(*) FROM audit_logs WHERE action = 'initial-admin-created';"]).stdout.trim()
  assert(audit === '1', 'Initial admin audit snapshot is missing.')
  console.log('PASS: admin bootstrap/refusal, login/authorization, Source token, idempotency, audited creation, and Worker delivery.')
} finally {
  docker(['rm', '-f', receiver], undefined, true)
}
