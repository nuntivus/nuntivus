import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { test } from 'node:test'

const result = spawnSync('docker', ['compose', '-f', 'docker-compose.quickstart.yml', 'config', '--format', 'json'], {
  cwd: fileURLToPath(new URL('../../', import.meta.url)),
  encoding: 'utf8',
  env: { ...process.env, NUNTIVUS_IMAGE_TAG: '0.1.0', NUNTIVUS_API_PORT: '8080', NUNTIVUS_DASHBOARD_PORT: '5173' },
  timeout: 30_000,
})
assert.equal(result.status, 0, 'Quick-start Compose configuration must validate.')
const config = JSON.parse(result.stdout)
const services = config.services

test('quick start builds application images locally and retains infrastructure images', () => {
  for (const component of ['api', 'worker', 'dashboard']) {
    assert.equal(services[component].image, `nuntivus-${component}:0.1.0`)
    assert.equal(services[component].pull_policy, 'build')
    const project = component === 'api' ? 'Api' : component === 'worker' ? 'Worker' : 'Dashboard'
    assert.equal(services[component].build.dockerfile, `NuntivusServer.${project}/Dockerfile`)
  }
  for (const name of ['migrate', 'bootstrap']) {
    assert.deepEqual(services[name].build, services.api.build)
    assert.equal(services[name].image, services.api.image)
    assert.equal(services[name].pull_policy, 'build')
  }
  for (const name of ['initialize', 'postgres', 'admin-check', 'nats']) {
    assert.ok(services[name].image)
    assert.equal(services[name].build, undefined)
  }
})

test('only the API and Dashboard publish ports, exclusively on loopback', () => {
  for (const [name, service] of Object.entries(services)) {
    if (['api', 'dashboard'].includes(name)) {
      assert.equal(service.ports.length, 1)
      assert.equal(service.ports[0].host_ip, '127.0.0.1')
    } else {
      assert.equal(service.ports, undefined)
    }
  }
  assert.equal(services.dashboard.environment.NUNTIVUS_API_BASE_URL, 'http://localhost:8080')
})

test('startup orders secret generation, migration, admin setup, and readiness', () => {
  assert.equal(services.postgres.depends_on.initialize.condition, 'service_completed_successfully')
  assert.equal(services.nats.depends_on.initialize.condition, 'service_completed_successfully')
  assert.equal(services.migrate.depends_on.postgres.condition, 'service_healthy')
  assert.equal(services['admin-check'].depends_on.migrate.condition, 'service_completed_successfully')
  assert.equal(services.bootstrap.depends_on['admin-check'].condition, 'service_completed_successfully')
  assert.equal(services.api.depends_on.bootstrap.condition, 'service_completed_successfully')
  assert.equal(services.dashboard.depends_on.api.condition, 'service_healthy')
  assert.equal(services.worker.depends_on.nats.condition, 'service_healthy')
  assert.equal(services.worker.depends_on.migrate.condition, 'service_completed_successfully')
})

test('generated secrets are not hardcoded and access is separated', () => {
  assert.equal(services.postgres.environment.POSTGRES_PASSWORD, undefined)
  assert.equal(services.postgres.environment.POSTGRES_PASSWORD_FILE, '/run/nuntivus-postgres/password')
  assert.equal(services.api.environment.LocalAuth__SigningKey, undefined)
  assert.equal(services.api.environment.DestinationCredentials__EncryptionKey, undefined)
  assert.equal(services.worker.environment.LocalAuth__SigningKey, undefined)
  assert.deepEqual(services.worker.volumes.map(volume => volume.source), ['shared-secrets'])
  assert.equal(services.worker.volumes[0].read_only, true)
  assert.equal(services.dashboard.volumes, undefined)
  assert.match(services.initialize.command[0], /\/dev\/urandom/)
  assert.match(services.initialize.command[0], /if \[ ! -e/)
  assert.match(services.initialize.command[0], /chmod 700/)
  assert.match(services['admin-check'].command[0], /SELECT COUNT\(\*\) FROM admin_users/)
  assert.match(services.bootstrap.command[2], /--create-admin < \/run\/nuntivus-api\/admin-password/)
})
