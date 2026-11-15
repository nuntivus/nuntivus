import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { test } from 'node:test'

function compose(files, service) {
  const result = spawnSync('docker', [
    'compose', '--env-file', '.env.example',
    ...files.flatMap(file => ['-f', file]),
    'config', '--format', 'json', ...(service ? [service] : []),
  ], {
    cwd: fileURLToPath(new URL('../../', import.meta.url)),
    encoding: 'utf8',
    env: {
      ...process.env,
      NUNTIVUS_IMAGE_TAG: 'deployment-test',
      NUNTIVUS_POSTGRES_PASSWORD: 'test-only-password',
      NUNTIVUS_DESTINATION_CREDENTIALS_KEY: 'test-only-key',
      NUNTIVUS_DATABASE_CONNECTION_STRING: 'Host=db.internal;Database=nuntivus;Username=nuntivus;Password=test-only-password',
      NUNTIVUS_NATS_URL: 'nats://broker.internal:4222',
      NUNTIVUS_NATS_TOKEN: 'test-only-token',
    },
    timeout: 30_000,
  })
  assert.equal(result.status, 0, `Compose configuration must validate: ${result.stderr}`)
  return JSON.parse(result.stdout).services
}

for (const file of ['docker-compose.yml', 'docker-compose.prod.yml']) {
  test(`${file} builds applications without pulling their images`, () => {
    const services = compose([file])
    for (const name of ['api', 'worker', 'migrate']) {
      assert.equal(services[name].pull_policy, 'never')
      assert.ok(services[name].build)
    }
    assert.equal(services.postgres.build, undefined)
    assert.equal(services.nats.build, undefined)
  })
}

for (const name of ['api', 'worker']) {
  test(`production ${name} can start without the other application`, () => {
    const services = compose(['docker-compose.prod.yml'], name)
    assert.ok(services[name])
    assert.ok(services.postgres)
    assert.ok(services.migrate)
    assert.equal(services[name === 'api' ? 'worker' : 'api'], undefined)
  })
}

for (const name of ['api', 'worker', 'migrate']) {
  test(`external ${name} has no local service dependencies`, () => {
    const services = compose(['docker-compose.prod.yml', 'docker-compose.external.yml'], name)
    assert.deepEqual(Object.keys(services), [name])
    assert.equal(Object.keys(services[name].depends_on ?? {}).length, 0)
    assert.match(services[name].environment.Database__PostgresConnectionString, /Host=db.internal;/)
    assert.equal(services[name].environment.DestinationCredentials__EncryptionKey, 'test-only-key')
    if (name === 'worker') {
      assert.equal(services.worker.environment.Nats__Url, 'nats://broker.internal:4222')
      assert.equal(services.worker.environment.Nats__Token, 'test-only-token')
      assert.equal(services.worker.ports, undefined)
    }
  })
}
