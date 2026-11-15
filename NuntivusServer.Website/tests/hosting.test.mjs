import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'
import { test } from 'node:test'

const context = vm.createContext({})
vm.runInContext(await readFile(new URL('../hosting/cloudfront-function.js', import.meta.url), 'utf8'), context)
for (const [uri, expected] of [['/', '/index.html'], ['/docs/intro/', '/docs/intro/index.html'], ['/blog/', '/blog/index.html'], ['/assets/js/main.123.js', '/assets/js/main.123.js'], ['/404.html', '/404.html'], ['/robots.txt', '/robots.txt'], ['/sitemap.xml', '/sitemap.xml']]) {
  test(`CloudFront maps ${uri} to ${expected}`, () => {
    assert.equal(context.handler({ request: { uri } }).uri, expected)
  })
}

for (const [host, uri, expected] of [
  ['nuntivus.com', '/', 'https://www.nuntivus.com/'],
  ['nuntivus.com', '/docs/intro', 'https://www.nuntivus.com/docs/intro/'],
  ['www.nuntivus.com', '/docs/intro', 'https://www.nuntivus.com/docs/intro/'],
  ['nuntivus.com', '/sitemap.xml', 'https://www.nuntivus.com/sitemap.xml'],
]) {
  test(`CloudFront permanently redirects ${host}${uri} to the canonical URL`, () => {
    const response = context.handler({ request: { uri, headers: { host: { value: host } }, querystring: {} } })
    assert.equal(response.statusCode, 301)
    assert.equal(response.headers.location.value, expected)
  })
}

test('canonical redirects retain encoded and repeated query parameters', () => {
  const response = context.handler({ request: {
    uri: '/docs/intro', headers: { host: { value: 'nuntivus.com' } },
    querystring: { q: { value: 'event%20delivery' }, tag: { multiValue: [{ value: 'http' }, { value: 'kafka' }] } },
  } })
  assert.equal(response.headers.location.value, 'https://www.nuntivus.com/docs/intro/?q=event%20delivery&tag=http&tag=kafka')
})
