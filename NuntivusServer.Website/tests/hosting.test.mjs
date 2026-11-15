import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'
import { test } from 'node:test'

const context = vm.createContext({})
vm.runInContext(await readFile(new URL('../hosting/cloudfront-function.js', import.meta.url), 'utf8'), context)
for (const [uri, expected] of [['/', '/index.html'], ['/docs/intro', '/docs/intro/index.html'], ['/blog/', '/blog/index.html'], ['/assets/js/main.123.js', '/assets/js/main.123.js'], ['/404.html', '/404.html']]) {
  test(`CloudFront maps ${uri} to ${expected}`, () => {
    assert.equal(context.handler({ request: { uri } }).uri, expected)
  })
}
