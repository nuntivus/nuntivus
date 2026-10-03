export const sourceFields: Record<string, string[]> = {
  RABBITMQ: ['uri', 'queue', 'username', 'secretRef'],
  KAFKA: ['bootstrapServers', 'topic', 'groupId', 'username', 'secretRef'],
  SQS: ['region', 'queueUrl', 'accessKeyId', 'secretRef'],
  NATS: ['url', 'stream', 'subject', 'durable', 'secretRef'],
  HTTP_POLLING: ['url', 'secretRef', 'intervalSeconds'],
  FOLDER: ['path', 'intervalSeconds'],
  S3: ['region', 'bucket', 'prefix', 'accessKeyId', 'secretRef', 'intervalSeconds'],
}

export const sourceTypes = ['HTTP_API', ...Object.keys(sourceFields)]
