import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const launch = JSON.parse(await readFile(new URL('../.vscode/launch.json', import.meta.url), 'utf8'))
const tasks = JSON.parse(await readFile(new URL('../.vscode/tasks.json', import.meta.url), 'utf8'))
const configuration = launch.configurations.find(item => item.name === 'Launch Website')
const startup = tasks.tasks.find(item => item.label === configuration.preLaunchTask)

test('website debugger starts the server without an automatic second browser', () => {
  assert.ok(startup)
  assert.equal(configuration.url, 'http://localhost:5180')
  assert.equal(startup.command, 'npm start -- --no-open')
  assert.equal(startup.options.cwd, '${workspaceFolder}')
  assert.equal(startup.isBackground, true)
})

test('website debugger waits for successful compilation, not the early server URL', () => {
  const readiness = startup.problemMatcher.background
  assert.equal(readiness.activeOnStart, true)
  assert.match('[INFO] Starting the development server...', new RegExp(readiness.beginsPattern))
  const ready = new RegExp(readiness.endsPattern)
  assert.doesNotMatch('[SUCCESS] Docusaurus website is running at: http://localhost:5180/', ready)
  assert.doesNotMatch('client (webpack 5.112.2) compiled with errors', ready)
  assert.match('client (webpack 5.112.2) compiled successfully', ready)
})
