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

test('repository links resolve from source on a clean checkout', async t => {
  const root = await fixture(t, {
    'README.md': '[Architecture](docs/architecture.md#overview)',
    'docs/architecture.md': '![Diagram](assets/flow.svg)',
    'docs/assets/flow.svg': '<svg />',
    'docs/self-hosting/README.md': '[Architecture](../architecture.md)',
    'docs/features/contract slos.md': '# Contract SLOs',
    'docs/quick-start.md': '[Architecture](architecture.md) [Self-hosting](self-hosting/README.md) [Encoded](features/contract%20slos.md)',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 5, failures: 0 })
  assert.deepEqual(errors, [])
})

test('missing repository links fail while external links and fenced examples are ignored', async t => {
  const root = await fixture(t, {
    'README.md': '[Missing](docs/missing.md) [External](https://example.com) [Anchor](#heading)\n```md\n[Example](not-a-real-file.md)\n```',
    'docs/intro.md': '[Missing reference](missing.md)',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 2, failures: 2 })
  assert.equal(errors.length, 2)
  assert.ok(errors.some(error => error.includes('missing local target docs/missing.md')))
  assert.ok(errors.some(error => error.includes('missing local target missing.md')))
})

test('dependency and build directories are excluded from documentation checks', async t => {
  const root = await fixture(t, {
    'docs/architecture.md': '# Architecture',
    'docs/intro.md': '[Missing](deleted.md)',
    'node_modules/package/README.md': '[Dependency link](missing.md)',
    'Example/bin/output.md': '[Build link](missing.md)',
    'Example/obj/cache.md': '[Cache link](missing.md)',
  })
  const errors = []
  assert.deepEqual(await checkDocLinks(root, error => errors.push(error)), { filesChecked: 2, failures: 1 })
  assert.equal(errors.length, 1)
  assert.ok(errors[0].includes('missing local target deleted.md'))
})
