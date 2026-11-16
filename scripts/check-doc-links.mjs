import { readdir, readFile, access } from 'node:fs/promises'
import { dirname, resolve, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const excluded = new Set(['.git', '.vs', 'node_modules', 'bin', 'obj', 'dist', 'backups', 'TestResults', 'test-results', 'playwright-report'])

export async function checkDocLinks(root, report = console.error) {
  root = resolve(root)
  let failures = 0
  let filesChecked = 0

  async function check(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = resolve(directory, entry.name)
      if (entry.isDirectory() && !excluded.has(entry.name)) await check(path)
      else if (entry.isFile() && entry.name.endsWith('.md')) {
        filesChecked++
        const markdown = (await readFile(path, 'utf8')).replace(/```[\s\S]*?```/g, '')
        for (const match of markdown.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
          const link = match[1].split(/\s+["']/)[0].replace(/^<|>$/g, '')
          if (/^(?:https?:|mailto:|#)/i.test(link)) continue
          const target = link.split('#')[0]
          if (!target) continue
          try { await access(resolve(dirname(path), decodeURIComponent(target))) }
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
