import assert from 'node:assert/strict'
import { test } from 'node:test'
import { normalizeConfiguration } from '../src/features/contracts/destination-forms/normalize-configuration.ts'
import { hydrateDestinationForm, mergeDestinationConfiguration } from '../src/features/contracts/destination-forms/stored-configuration.ts'
import { httpHeaderRows, httpHeaders, mapDestinationFormToConfig, normalizeDestinationType, validateDestinationFormState } from '../src/features/contracts/destination-forms/types.ts'

test('shared destination editor normalizes stored configuration and preserves masked credentials', () => {
  const stored = {
    Url: 'https://hooks.example.com/one',
    Authentication: { Type: 'bearer', Credential: { Kind: 'ref', Value: '***' } },
    TimeoutSeconds: 30,
  }
  assert.deepEqual(normalizeConfiguration(stored), {
    url: 'https://hooks.example.com/one',
    authentication: { type: 'bearer', credential: { kind: 'ref', value: '***' } },
    timeoutSeconds: 30,
  })
})

test('HTTP destination method, headers and timeout round-trip without changing header names', () => {
  const stored = JSON.stringify({ Url: 'https://example.com/hook', Method: 'PATCH', Authentication: { Type: 'none' },
    Headers: { 'X-API-Key': 'public-value' }, TimeoutSeconds: 15 })
  const form = hydrateDestinationForm('Http', stored)
  assert.equal(form.http.method, 'PATCH')
  assert.equal(form.http.timeoutSeconds, '15')
  assert.deepEqual(JSON.parse(form.http.headers), { 'X-API-Key': 'public-value' })
  form.http.method = 'DELETE'
  form.http.headers = '{"X-Region":"west"}'
  assert.equal(validateDestinationFormState(form), null)
  const merged = mergeDestinationConfiguration(stored, form)
  assert.equal(merged.method, 'DELETE')
  assert.deepEqual(merged.headers, { 'X-Region': 'west' })
  assert.equal(merged.timeoutSeconds, 15)
  assert.equal(mapDestinationFormToConfig(form).method, 'DELETE')
})

test('HTTP header rows retain exact names and require unique, completed keys', () => {
  const form = hydrateDestinationForm('Http', JSON.stringify({ Url: 'https://example.com/hook', Headers: { 'X-Region': 'west' } }))
  assert.deepEqual(httpHeaderRows(form.http.headers), [{ key: 'X-Region', value: 'west' }])
  form.http.headers = JSON.stringify([{ key: 'X-Region', value: 'east' }, { key: 'X-Trace', value: '' }])
  assert.equal(validateDestinationFormState(form), null)
  assert.deepEqual(httpHeaders(form.http.headers), { 'X-Region': 'east', 'X-Trace': '' })
  assert.deepEqual(mapDestinationFormToConfig(form).headers, { 'X-Region': 'east', 'X-Trace': '' })
  form.http.headers = JSON.stringify([{ key: 'X-Region', value: 'east' }, { key: '', value: '1' }])
  assert.match(validateDestinationFormState(form), /name/)
  form.http.headers = JSON.stringify([{ key: 'X-Region', value: 'east' }, { key: 'x-region', value: '1' }])
  assert.match(validateDestinationFormState(form), /unique/)
  form.http.headers = '[]'
  assert.deepEqual(mapDestinationFormToConfig(form).headers, {})
})

test('destination edit supports numeric API enum types and normalized configuration', () => {
  const item = {
    type: 1,
    configuration: JSON.stringify({ Url: 'https://hooks.example.com/one', Authentication: { Type: 'none' } }),
  }
  assert.equal(hydrateDestinationForm(normalizeDestinationType(item.type), item.configuration).http.url, 'https://hooks.example.com/one')
  assert.equal(normalizeDestinationType(0), null)
})

test('typed HTTP form keeps credentials and protected headers when editing URL', () => {
  const stored = JSON.stringify({ Url: 'https://example.com/old', Authentication: {
    Type: 'bearer', Credential: { Kind: 'ref', Value: '***' },
  }, Headers: { 'X-Region': 'west' }, SecretHeaders: { 'X-Token': '***' }, TimeoutSeconds: 30 })
  const form = hydrateDestinationForm('Http', stored)
  assert.equal(form.http.credential.mode, 'keep')
  assert.equal(form.http.authenticationType, 'bearer')
  form.http.url = 'https://example.com/new'
  const merged = mergeDestinationConfiguration(stored, form)
  assert.equal(merged.url, 'https://example.com/new')
  assert.deepEqual(merged.headers, { 'X-Region': 'west' })
  assert.equal(merged.timeoutSeconds, 30)
  assert.equal('secretHeaders' in merged, false)
  assert.deepEqual(merged.authentication, { type: 'bearer', credential: { kind: 'ref', value: '***' } })
})

test('existing HTTP destination sends its type discriminator before stored fields', () => {
  const stored = JSON.stringify({ Url: 'https://www.google.com', Authentication: { Type: 'none' } })
  const form = hydrateDestinationForm('Http', stored)
  const configuration = mergeDestinationConfiguration(stored, form)
  assert.equal(Object.keys(configuration)[0], 'type')
  assert.match(JSON.stringify({ configuration }), /^\{"configuration":\{"type":"http",/)
  assert.equal(configuration.type, 'http')
})

test('typed Kafka, NATS, RabbitMQ and SQS forms hydrate and retain nested credentials', () => {
  const cases = [
    ['Kafka', { BootstrapServers: 'broker:9092', Topic: 'events', Credentials: { Username: 'agent', Password: { Kind: 'ref', Value: '***' } } }, 'kafka', 'bootstrapServers'],
    ['Nats', { Url: 'nats://localhost:4222', Subject: 'events', Credentials: { Token: { Kind: 'ref', Value: '***' } } }, 'nats', 'subject'],
    ['RabbitMq', { Uri: 'amqp://localhost', Exchange: 'events', RoutingKey: 'orders', Credentials: { Password: { Kind: 'ref', Value: '***' } } }, 'rabbitMq', 'exchange'],
    ['Sqs', { Region: 'us-east-1', QueueUrl: 'https://sqs.example.com/one', Credentials: { SecretAccessKey: { Kind: 'ref', Value: '***' } } }, 'sqs', 'region'],
  ]
  for (const [type, config, section, key] of cases) {
    const stored = JSON.stringify(config)
    const form = hydrateDestinationForm(type, stored)
    assert.ok(form[section][key])
    const merged = mergeDestinationConfiguration(stored, form)
    assert.equal(Object.keys(merged)[0], 'type')
    assert.deepEqual(JSON.parse(JSON.stringify(merged)).credentials, normalizeConfiguration(config).credentials)
  }
})

test('typed destination editor clears credentials selected for removal', () => {
  const stored = JSON.stringify({ Uri: 'amqp://localhost', Exchange: 'events', RoutingKey: 'orders',
    Credentials: { Username: 'agent', Password: { Kind: 'ref', Value: '***' } } })
  const form = hydrateDestinationForm('RabbitMq', stored)
  form.rabbitMq.password.mode = 'remove'
  assert.equal(mergeDestinationConfiguration(stored, form).credentials.password, null)
})

test('resetting typed credentials to stored value does not clear masked secrets', () => {
  const http = JSON.stringify({ Url: 'https://example.com/hook', Authentication: {
    Type: 'bearer', Credential: { Kind: 'inline', Value: '***' },
  } })
  const httpForm = hydrateDestinationForm('Http', http)
  httpForm.http.credential.value = ''
  assert.equal(mergeDestinationConfiguration(http, httpForm).authentication.credential.value, '***')

  const kafka = JSON.stringify({ BootstrapServers: 'broker:9092', Topic: 'events',
    Credentials: { Password: { Kind: 'inline', Value: '***' } } })
  const kafkaForm = hydrateDestinationForm('Kafka', kafka)
  kafkaForm.kafka.password.value = ''
  assert.equal(mergeDestinationConfiguration(kafka, kafkaForm).credentials.password.value, '***')
})

test('a new draft has empty settings but hydrates its selected type for the detail editor', () => {
  const draft = hydrateDestinationForm('Kafka', '{}')
  assert.equal(draft.type, 'Kafka')
  assert.equal(draft.kafka.bootstrapServers, '')
  assert.equal(draft.kafka.topic, '')
  draft.kafka.bootstrapServers = 'broker:9092'
  draft.kafka.topic = 'orders'
  assert.equal(mergeDestinationConfiguration('{}', draft).bootstrapServers, 'broker:9092')
})
