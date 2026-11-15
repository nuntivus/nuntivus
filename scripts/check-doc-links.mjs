import { readdir, readFile, access, stat } from 'node:fs/promises'
import { dirname, resolve, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const excluded = new Set(['.git', '.vs', 'node_modules', 'bin', 'obj', 'dist', 'backups', 'TestResults', 'test-results', 'playwright-report'])
function isWithin(directory, path) {
  return path === directory || path.startsWith(`${directory}${sep}`)
}

export async function checkDocLinks(root, report = console.error) {
  root = resolve(root)
  const website = resolve(root, 'NuntivusServer.Website')
  const websiteDocs = resolve(website, 'docs')
  const reference = resolve(websiteDocs, 'reference')
  const generated = [reference, resolve(website, 'build'), resolve(website, '.docusaurus')]
  let failures = 0
  let filesChecked = 0

  async function checkTarget(path, target) {
    const decoded = decodeURIComponent(target)
    const route = isWithin(website, path) && decoded.startsWith('/docs/')
    let destination = route
      ? resolve(websiteDocs, decoded.slice('/docs/'.length))
      : resolve(dirname(path), decoded)
    if (isWithin(website, path) && isWithin(reference, destination)) {
      destination = resolve(root, 'docs', relative(reference, destination))
    }
    if (!route) return access(destination)
    const candidates = [destination, `${destination}.md`, resolve(destination, 'README.md'), resolve(destination, 'index.md')]
    for (const candidate of candidates) {
      try {
        if (candidate.endsWith('.md') && (await stat(candidate)).isFile()) return
      } catch (error) {
        if (error.code !== 'ENOENT' && error.code !== 'ENOTDIR') throw error
      }
    }
    throw new Error('No Markdown source for documentation route')
  }

  async function check(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = resolve(directory, entry.name)
      if (entry.isDirectory() && !excluded.has(entry.name) && !generated.includes(path)) await check(path)
      else if (entry.isFile() && entry.name.endsWith('.md')) {
        filesChecked++
        const markdown = (await readFile(path, 'utf8')).replace(/```[\s\S]*?```/g, '')
        for (const match of markdown.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
          const link = match[1].split(/\s+["']/)[0].replace(/^<|>$/g, '')
          if (/^(?:https?:|mailto:|#)/i.test(link)) continue
          const target = link.split('#')[0]
          if (!target) continue
          try { await checkTarget(path, target) }
          catch { report(`${relative(root, path)}: missing local target ${target}`); failures++ }
        }
      }
    }
  }
  await check(root)
  return { filesChecked, failures }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { filesChecked, failures } = await checkDocLinks(resolve(import.meta.dirname, '..'))
  console.log(`Checked ${filesChecked} Markdown files; ${failures} missing local link targets.`)
  process.exitCode = failures ? 1 : 0
}
