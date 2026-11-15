import assert from 'node:assert/strict'
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { checkDocLinks } from '../../scripts/check-doc-links.mjs'

async function fixture(t, files) {
  const root = await mkdtemp(join(tmpdir(), 'nuntivus-doc-links-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  for (const [name, content] of Object.entries(files)) {
    const path = join(root, name)
    await mkdir(dirname(path), { recursive: true })
    await writeFile(path, content)
  }
  return root
}

test('website links resolve from source on a clean checkout', async t => {
  const root = await fixture(t, {
    'README.md': '[Architecture](docs/architecture.md#overview)',
    'docs/architecture.md': '![Diagram](assets/flow.svg)',
    'docs/assets/flow.svg': '<svg />',
    'docs/self-hosting/README.md': '# Self-hosting',
    'docs/features/contract slos.md': '# Contract SLOs',
    'NuntivusServer.Website/docs/quick-start.md': '[Architecture](reference/architecture.md) [Self-hosting](reference/self-hosting/README.md)',
    'NuntivusServer.Website/blog/intro.md': '[Quick start](/docs/quick-start) [Architecture](/docs/reference/architecture#overview) [Directory](/docs/reference/self-hosting/) [Encoded](/docs/reference/features/contract%20slos) [File](/docs/reference/architecture.md)',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 6, failures: 0 })
  assert.deepEqual(errors, [])
})

test('missing website routes, generated references and repository links still fail', async t => {
  const root = await fixture(t, {
    'README.md': '[Missing](docs/missing.md) [External](https://example.com) [Anchor](#heading)\n```md\n[Example](not-a-real-file.md)\n```',
    'NuntivusServer.Website/docs/intro.md': '[Missing reference](reference/missing.md)',
    'NuntivusServer.Website/blog/intro.md': '[Missing route](/docs/reference/missing) [Missing page](/docs/missing) [Empty directory](/docs/reference/empty)',
    'docs/empty/.keep': '',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 3, failures: 5 })
  assert.equal(errors.length, 5)
  assert.ok(errors.some(error => error.includes('missing local target /docs/reference/missing')))
  assert.ok(errors.some(error => error.includes('missing local target reference/missing.md')))
})

test('generated website output is ignored and cannot hide missing source documentation', async t => {
  const root = await fixture(t, {
    'docs/architecture.md': '# Architecture',
    'NuntivusServer.Website/docs/intro.md': '[Stale reference](reference/deleted.md)',
    'NuntivusServer.Website/docs/reference/deleted.md': '[Generated link](missing.md)',
    'NuntivusServer.Website/build/index.md': '[Build link](missing.md)',
    'NuntivusServer.Website/.docusaurus/cache.md': '[Cache link](missing.md)',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 2, failures: 1 })
  assert.equal(errors.length, 1)
  assert.ok(errors[0].includes('missing local target reference/deleted.md'))
})
