import { spawnSync, spawn } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

process.chdir(resolve(dirname(fileURLToPath(import.meta.url)), '../..'))
const project = `nuntivus-perf-${randomBytes(5).toString('hex')}`
const compose = ['compose', '-p', project, '-f', 'docker-compose.yml', '-f', 'tests/performance/compose.performance.yml']
const samples = []
let sampling = false
const password = randomBytes(24).toString('base64url')
const username = 'performance-admin'
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))
function docker(args, input, allowFailure = false) {
  const result = spawnSync('docker', args, { input, encoding: 'utf8', timeout: 600000, maxBuffer: 16 * 1024 * 1024 })
  if (!allowFailure && (result.error || result.status !== 0)) throw new Error(`Docker ${args.slice(0, 3).join(' ')} failed; inspect tests/performance/results/pipeline-local/docker.log`)
  return result
}
async function sampleResources() {
  sampling = true
  while (sampling) {
    const ids = docker(['ps', '-q', '--filter', `label=com.docker.compose.project=${project}`]).stdout.trim().split(/\s+/).filter(Boolean)
    if (ids.length) {
      const stats = docker(['stats', '--no-stream', '--format', '{{json .}}', ...ids], undefined, true)
      for (const line of stats.stdout.trim().split('\n').filter(Boolean))
        samples.push({ measuredAtUtc: new Date().toISOString(), ...JSON.parse(line) })
    }
    await sleep(1000)
  }
}
await mkdir('tests/performance/results/pipeline-local', { recursive: true })
let sampler
try {
  console.log(`Starting isolated Docker project ${project}; existing stacks are not modified.`)
  const up = docker([...compose, 'up', '-d', '--build', '--wait', 'api', 'worker', 'receiver'])
  await writeFile('tests/performance/results/pipeline-local/docker.log', up.stdout + up.stderr)
  docker([...compose, 'exec', '-T', '-e', `AdminBootstrap__Username=${username}`, 'api', 'dotnet', 'NuntivusServer.Api.dll', '--create-admin'], `${password}\n`)
  const login = docker([...compose, 'exec', '-T', 'api', 'sh', '-ec',
    'umask 077; cat > /tmp/perf-login.json; trap "rm -f /tmp/perf-login.json" EXIT; wget -qO- --header="Content-Type: application/json" --post-file=/tmp/perf-login.json http://127.0.0.1:8080/v1/auth/local/login'], JSON.stringify({ username, password }))
  process.env.PERF_ADMIN_TOKEN = JSON.parse(login.stdout).accessToken
  process.env.PERF_RUN_ID = project
  sampler = sampleResources()
  await new Promise((resolve, reject) => {
    const child = spawn('docker', [...compose, 'run', '--rm', '-T', '-e', 'PERF_ADMIN_TOKEN', 'loadgen'], { stdio: 'inherit', env: process.env })
    child.once('error', reject)
    child.once('exit', code => code === 0 ? resolve() : reject(new Error(`Load generator failed with exit ${code}`)))
  })
  const counts = docker([...compose, 'exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', 'nuntivus', '-Atc',
    "SELECT json_build_object('events',(SELECT count(*) FROM events),'deliveryStatuses',(SELECT json_object_agg(status,n) FROM (SELECT status,count(*) n FROM deliveries GROUP BY status) x));"])
  await writeFile('tests/performance/results/pipeline-local/database.json', counts.stdout.trim())
  const state = JSON.parse(counts.stdout)
  const expected = 20 + Number(process.env.PERF_EVENTS ?? 500) * (process.env.PERF_CONCURRENCY ?? '1,8,32').split(',').length
  if (state.events !== expected || state.deliveryStatuses?.Delivered !== expected) throw new Error('Persisted event/delivery counts did not match the workload')
  console.log('PASS: exact persisted event and Delivered counts verified.')
} finally {
  sampling = false
  if (sampler) await sampler
  delete process.env.PERF_ADMIN_TOKEN
  await writeFile('tests/performance/results/pipeline-local/resources.json', JSON.stringify({ project, samples }, null, 2))
  const logs = docker([...compose, 'logs', '--no-color', 'api', 'worker'], undefined, true)
  await writeFile('tests/performance/results/pipeline-local/services.log', logs.stdout + logs.stderr)
  docker([...compose, 'down', '--volumes', '--remove-orphans'], undefined, true)
  console.log(`Removed only owned project ${project} and its volumes.`)
}
