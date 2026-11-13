import { createServer } from 'node:http'
import { randomBytes } from 'node:crypto'
import { mkdir, writeFile } from 'node:fs/promises'
import { performance } from 'node:perf_hooks'

const percentile = (values, fraction) => {
  const sorted = [...values].sort((a, b) => a - b)
  return sorted.length ? sorted[Math.ceil(sorted.length * fraction) - 1] : null
}
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))
if (process.argv[2] === 'receiver') {
  const runs = new Map()
  createServer(async (request, response) => {
    try {
      if (request.method === 'GET' && request.url === '/metrics') {
        response.setHeader('Content-Type', 'application/json')
        response.end(JSON.stringify(Object.fromEntries([...runs].map(([key, value]) => [key, {
          received: value.ids.size, duplicates: value.duplicates, latencies: value.latencies,
          lastReceivedAt: value.lastReceivedAt
        }]))))
        return
      }
      if (request.method !== 'POST') { response.writeHead(405).end(); return }
      const chunks = []
      let bytes = 0
      for await (const chunk of request) {
        bytes += chunk.length
        if (bytes > 1024 * 1024) throw new Error('Oversized request')
        chunks.push(chunk)
      }
      const payload = JSON.parse(Buffer.concat(chunks).toString())
      const eventId = request.headers['x-nuntivus-event-id']
      if (!payload.runId || !eventId || !Number.isFinite(payload.sentAt)) throw new Error('Invalid stress delivery')
      const value = runs.get(payload.runId) ?? { ids: new Set(), duplicates: 0, latencies: [], lastReceivedAt: 0 }
      if (value.ids.has(eventId)) value.duplicates++
      else {
        value.ids.add(eventId)
        value.latencies.push(Date.now() - payload.sentAt)
        value.lastReceivedAt = Date.now()
      }
      runs.set(payload.runId, value)
      response.writeHead(200).end('accepted')
    } catch { response.writeHead(400).end('invalid stress payload') }
  }).listen(8080, '0.0.0.0')
} else {
  const admin = process.env.PERF_ADMIN_TOKEN
  if (!admin) throw new Error('PERF_ADMIN_TOKEN is required; use run-pipeline.mjs')
  const sourceCredential = randomBytes(24).toString('base64url')
  const count = Number(process.env.PERF_EVENTS ?? 500)
  const levels = (process.env.PERF_CONCURRENCY ?? '1,8,32').split(',').map(Number)
  if (!Number.isInteger(count) || count < 1 || count > 100000 || levels.some(c => !Number.isInteger(c) || c < 1 || c > 256))
    throw new Error('Invalid workload bounds')
  async function request(path, body, authenticated = true) {
    const response = await fetch(`http://api:8080${path}`, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { 'Content-Type': 'application/json', ...(authenticated
        ? { Authorization: `Bearer ${admin}` } : { 'X-Nuntivus-Source-Token': sourceCredential }) },
      body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000)
    })
    if (!response.ok) throw new Error(`API ${path} returned ${response.status}`)
    return response.status === 204 ? undefined : response.json()
  }
  await request('/v1/customers', { id: 'performance', name: 'Performance' })
  await request('/v1/admin/secrets', { name: 'secret.source.performance', value: sourceCredential })
  const source = await request('/v1/sources', { name: 'Performance HTTP source', type: 'HTTP_API',
    configuration: { authentication: { type: 'token', secretRef: 'secret.source.performance' } } })
  const destination = await request('/v1/destinations', { name: 'Performance HTTP receiver', configuration: { type: 'http', url: 'http://receiver:8080/', authentication: { type: 'none' } } })
  const contract = await request('/v1/contracts', { customerId: 'performance', name: 'Performance', eventTypes: ['stress.created'], sourceIds: [source.id], destinationIds: [destination.id] })
  await request(`/v1/contracts/${contract.id}/enable`, {})
  const results = []
  async function run(concurrency, requested, warmup = false) {
    const runId = `${process.env.PERF_RUN_ID}-${warmup ? 'warmup' : concurrency}`
    const latencies = [], errors = [], ids = new Set()
    let next = 0
    const startedAt = Date.now(), started = performance.now()
    await Promise.all(Array.from({ length: concurrency }, async () => {
      while (next < requested) {
        const sequence = next++
        const before = performance.now()
        try {
          const accepted = await request(`/v1/sources/${source.id}/events`, {
            idempotencyKey: `${runId}-${sequence}`, type: 'stress.created', timestamp: new Date().toISOString(),
            payload: { runId, sequence, sentAt: Date.now(), data: 'x'.repeat(1024) }
          }, false)
          if (!accepted.id || ids.has(accepted.id)) throw new Error('Missing/duplicate accepted event ID')
          ids.add(accepted.id)
          latencies.push(performance.now() - before)
        } catch (error) { errors.push(error.message) }
      }
    }))
    const ingestionSeconds = (performance.now() - started) / 1000
    let metrics
    const deadline = Date.now() + 180000
    do {
      const response = await fetch('http://receiver:8080/metrics', { signal: AbortSignal.timeout(10000) })
      metrics = (await response.json())[runId]
      if ((metrics?.received ?? 0) >= ids.size) break
      await sleep(250)
    } while (Date.now() < deadline)
    const delivered = metrics?.received ?? 0
    const pipelineSeconds = delivered ? (metrics.lastReceivedAt - startedAt) / 1000 : (performance.now() - started) / 1000
    const result = {
      name: 'HTTP_API -> PostgreSQL/outbox -> JetStream -> matcher -> dispatcher -> HTTP receiver',
      runId, concurrency, requested, accepted: ids.size, delivered, errors: errors.length,
      duplicates: metrics?.duplicates ?? 0, payloadDataBytes: 1024, ingestionSeconds, pipelineSeconds,
      ingestionPerSecond: ids.size / ingestionSeconds, deliveredPerSecond: delivered / pipelineSeconds,
      apiP50Ms: percentile(latencies, .5), apiP95Ms: percentile(latencies, .95), apiP99Ms: percentile(latencies, .99),
      endToEndP50Ms: percentile(metrics?.latencies ?? [], .5), endToEndP95Ms: percentile(metrics?.latencies ?? [], .95),
      endToEndP99Ms: percentile(metrics?.latencies ?? [], .99), errorExamples: errors.slice(0, 3),
      measuredAtUtc: new Date().toISOString()
    }
    if (!warmup) { results.push(result); console.log(JSON.stringify(result)) }
    if (errors.length || delivered !== requested) throw new Error(`Workload failed: accepted ${ids.size}, delivered ${delivered}, errors ${errors.length}`)
  }
  try {
    await run(1, 20, true)
    for (const concurrency of levels) await run(concurrency, count)
  } finally {
    await mkdir('results/pipeline-local', { recursive: true })
    await writeFile('results/pipeline-local/measurements.json', JSON.stringify({
      runtime: process.version, warmupEvents: 20, results
    }, null, 2))
  }
}
