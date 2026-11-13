import { readdir } from 'node:fs/promises'

const files = await readdir(new URL('.', import.meta.url))
for (const file of files.sort()) {
  if (file.endsWith('.test.mjs') && !file.endsWith('.e2e.test.mjs') && file !== 'unit.test.mjs') {
    await import(new URL(file, import.meta.url))
  }
}
