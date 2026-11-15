import assert from 'node:assert/strict'
import { test } from 'node:test'
import { rewriteLinks } from '../scripts/sync-docs.mjs'

test('documentation links remain local when their target is imported', () => {
  assert.equal(rewriteLinks('[Architecture](../architecture.md#flow)', 'features/alerts.md'), '[Architecture](../architecture.md#flow)')
})
test('repository-only files resolve to GitHub without escaping the imported docs', () => {
  assert.equal(rewriteLinks('[README](../README.md)', 'README.md'), '[README](https://github.com/nuntivus/nuntivus-server/blob/main/README.md)')
  assert.equal(rewriteLinks('[Compose](../../docker-compose.yml)', 'self-hosting/README.md'), '[Compose](https://github.com/nuntivus/nuntivus-server/blob/main/docker-compose.yml)')
})
test('external links and local anchors are unchanged', () => {
  const input = '[Web](https://nuntivus.com) [Section](#section)'
  assert.equal(rewriteLinks(input, 'README.md'), input)
})
test('documentation images use locally copied assets at any document depth', () => {
  assert.equal(rewriteLinks('![Architecture](assets/architecture.svg)', 'architecture.md'), '![Architecture](/img/docs/assets/architecture.svg)')
  assert.equal(rewriteLinks('![Architecture](../assets/architecture.svg#title)', 'features/alerts.md'), '![Architecture](/img/docs/assets/architecture.svg#title)')
  assert.equal(rewriteLinks('![Outside](../../private.svg)', 'architecture.md'), '![Outside](https://github.com/nuntivus/nuntivus-server/blob/main/../private.svg)')
})
