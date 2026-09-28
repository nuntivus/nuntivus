import assert from 'node:assert/strict'
import { test } from 'node:test'
import { toManagedSecretReference } from '../src/features/contracts/managed-secret-name.ts'

test('accepts a short secret name and adds the required scheme', () => {
  assert.equal(toManagedSecretReference('test.test.erete'), 'secret.test.test.erete')
  assert.equal(toManagedSecretReference(' acme.webhook '), 'secret.acme.webhook')
})

test('keeps existing secret references unchanged', () => {
  assert.equal(toManagedSecretReference('secret.acme.webhook'), 'secret.acme.webhook')
})

test('rejects invalid paths and references longer than the backend limit', () => {
  assert.equal(toManagedSecretReference(''), null)
  assert.equal(toManagedSecretReference('secret.'), null)
  assert.equal(toManagedSecretReference('acme.bad name'), null)
  assert.equal(toManagedSecretReference('a'.repeat(250)), null)
})
