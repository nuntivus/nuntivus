export const sourceFields: Record<string, string[]> = {
  RABBITMQ: ['uri', 'queue', 'username', 'secretRef'],
  KAFKA: ['bootstrapServers', 'topic', 'groupId', 'username', 'secretRef'],
  SQS: ['region', 'queueUrl', 'accessKeyId', 'secretRef'],
  NATS: ['url', 'stream', 'subject', 'durable', 'secretRef'],
  HTTP_POLLING: ['url', 'method', 'intervalSeconds', 'secretRef', 'headers', 'body'],
  FOLDER: ['path', 'intervalSeconds'],
  S3: ['region', 'bucket', 'prefix', 'accessKeyId', 'secretRef', 'intervalSeconds'],
}

export const sourceTypes = ['HTTP_API', ...Object.keys(sourceFields)]

export function sourceConfiguration(type: string, authType: 'none' | 'token', secretRef: string,
  method: string, fields: Record<string, string>) {
  if (type === 'HTTP_API') return {
    authentication: authType === 'none' ? { type: 'none' as const } : { type: 'token' as const, secretRef }, method,
  }
  return Object.fromEntries((sourceFields[type] ?? []).filter(key => fields[key]?.trim())
    .map(key => [key, key === 'intervalSeconds' ? Number(fields[key])
      : key === 'headers' || key === 'body' ? JSON.parse(fields[key]) : fields[key].trim()]))
}

export function sourceConfigurationFields(configuration: Record<string, unknown>) {
  return Object.fromEntries(Object.entries(configuration).map(([key, value]) =>
    [key, key === 'headers' || key === 'body' ? JSON.stringify(value, null, 2) : String(value)]))
}

export function validatePollingJson(fields: Record<string, string>) {
  if (fields.headers?.trim()) {
    const headers: unknown = JSON.parse(fields.headers)
    if (!headers || typeof headers !== 'object' || Array.isArray(headers)
      || Object.values(headers).some(value => typeof value !== 'string'))
      throw new Error('Headers must be a JSON object with string values.')
  }
  if (fields.body?.trim()) {
    if (fields.method !== 'POST') throw new Error('Polling body requires POST.')
    const body: unknown = JSON.parse(fields.body)
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw new Error('Polling body must be a JSON object or array.')
  }
}
