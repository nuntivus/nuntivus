import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const build = resolve(import.meta.dirname, '../build')
const origin = 'https://www.nuntivus.com'
const robots = await readFile(resolve(build, 'robots.txt'), 'utf8')
assert.match(robots, /^User-agent: \*$/m)
assert.match(robots, /^Allow: \/$/m)
assert.ok(!/^Disallow:\s*\/\s*$/m.test(robots), 'Public crawling must not be disabled')
assert.ok(robots.includes(`Sitemap: ${origin}/sitemap.xml`))

const sitemap = await readFile(resolve(build, 'sitemap.xml'), 'utf8')
const urls = [...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(match => match[1])
assert.ok(urls.length > 0, 'Sitemap must contain public URLs')
assert.equal(new Set(urls).size, urls.length, 'Sitemap must not contain duplicates')
for (const path of ['/', '/docs/intro/', '/docs/quick-start/', '/docs/reference/architecture/', '/blog/']) {
  assert.ok(urls.includes(`${origin}${path}`), `Missing sitemap route: ${path}`)
}
for (const location of urls) {
  const url = new URL(location)
  assert.equal(url.origin, origin, `Noncanonical sitemap URL: ${location}`)
  assert.equal(url.search, '')
  assert.equal(url.hash, '')
  assert.ok(url.pathname.endsWith('/'), `Page URL needs trailing slash: ${location}`)
  assert.ok(!/^\/(search|404\.html)(\/|$)/.test(url.pathname), `Nonindexable route in sitemap: ${location}`)
  const html = await readFile(resolve(build, `.${decodeURIComponent(url.pathname)}`, 'index.html'), 'utf8')
  assert.ok(html.includes(`rel="canonical" href="${location}"`), `Missing canonical tag: ${location}`)
  assert.ok(!/<meta[^>]+name="robots"[^>]+content="[^"]*noindex/i.test(html), `Sitemap page is noindex: ${location}`)
}
const home = await readFile(resolve(build, 'index.html'), 'utf8')
assert.match(home, /<title\b[^>]*>[^<]+<\/title>/)
assert.match(home, /<meta[^>]+name="description"[^>]+content="[^"]+"/)
assert.ok(home.includes(`property="og:url" content="${origin}/"`), 'Homepage Open Graph URL must be canonical')
console.log(`SEO checks passed: robots.txt and ${urls.length} canonical, indexable sitemap pages.`)
