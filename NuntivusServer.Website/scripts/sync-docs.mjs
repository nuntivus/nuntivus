import { cp, mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const website = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const root = path.dirname(website)
const source = path.join(root, 'docs')
const output = path.join(website, 'docs/reference')
const repository = 'https://github.com/nuntivus/nuntivus-server/blob/main/'

export function rewriteLinks(markdown, relativeFile) {
  return markdown.replace(/(!?\[[^\]]*\]\()([^\s)]+)([^)]*\))/g, (match, prefix, target, suffix) => {
    if (/^(?:[a-z]+:|#|\/)/i.test(target)) return match
    const [file, anchor = ''] = target.split('#')
    const resolved = path.posix.normalize(path.posix.join('docs', path.posix.dirname(relativeFile), file))
    if (prefix.startsWith('!') && resolved.startsWith('docs/assets/')) return `${prefix}/img/${resolved}${anchor ? `#${anchor}` : ''}${suffix}`
    if (resolved.startsWith('docs/') && resolved.endsWith('.md') && !prefix.startsWith('!')) return match
    return `${prefix}${repository}${resolved}${anchor ? `#${anchor}` : ''}${suffix}`
  })
}

async function sync(directory, relative = '') {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const name = path.posix.join(relative, entry.name)
    if (entry.isDirectory()) await sync(path.join(directory, entry.name), name)
    else if (entry.name.endsWith('.md')) {
      const target = path.join(output, name)
      await mkdir(path.dirname(target), { recursive: true })
      const markdown = await readFile(path.join(directory, entry.name), 'utf8')
      await writeFile(target, `---\nformat: md\n---\n\n${rewriteLinks(markdown, name)}`)
    }
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await rm(output, { recursive: true, force: true })
  await mkdir(output, { recursive: true })
  await sync(source)
  await mkdir(path.join(website, 'static/img'), { recursive: true })
  await cp(path.join(source, 'assets'), path.join(website, 'static/img/docs/assets'), { recursive: true })
  await cp(path.join(root, 'NuntivusServer.Dashboard/src/assets/nuntivus-logo.svg'), path.join(website, 'static/img/logo.svg'))
  await cp(path.join(root, 'NuntivusServer.Dashboard/public/favicon.svg'), path.join(website, 'static/img/favicon.svg'))
  console.log('Imported repository docs and dashboard branding.')
}
