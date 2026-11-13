import assert from 'node:assert/strict'
import { test } from 'node:test'
import { resolveApiBaseUrl } from '../src/app/runtime-config.ts'

test('runtime API origin overrides build configuration', () => {
  assert.equal(resolveApiBaseUrl(' https://api.example.com/ ', 'https://old.example.com', false, true, 'dashboard.example.com'), 'https://api.example.com')
})

test('Vite API configuration and development fallback remain supported', () => {
  assert.equal(resolveApiBaseUrl(undefined, 'http://localhost:8080', true, false, 'localhost'), 'http://localhost:8080')
  assert.equal(resolveApiBaseUrl(undefined, undefined, true, false, 'localhost'), 'https://localhost:7123')
})

test('installed production Dashboard supports local API only from a local browser origin', () => {
  for (const host of ['localhost', '127.0.0.1', '[::1]']) {
    assert.equal(resolveApiBaseUrl(`http://${host}:8080`, undefined, false, true, host), `http://${host}:8080`)
    assert.throws(() => resolveApiBaseUrl(`http://${host}:8080`, undefined, false, true, 'dashboard.example.com'), /localhost API/)
  }
})

test('production runtime configuration requires a valid HTTP origin without credentials', () => {
  assert.throws(() => resolveApiBaseUrl(undefined, undefined, false, true, 'localhost'), /API base URL is required/)
  for (const value of ['not-a-url', 'javascript:alert(1)', 'ftp://api.example.com', 'https://user:password@api.example.com', 'https://api.example.com/v1', 'https://api.example.com?token=secret', 'https://api.example.com#fragment']) {
    assert.throws(() => resolveApiBaseUrl(value, undefined, false, true, 'localhost'))
  }
})
