import { Text, TextField } from '@radix-ui/themes'
import { SecretField } from './secret-field'
import type { DestinationFormState } from './types'

type SqsDestinationFormProps = {
  value: DestinationFormState['sqs']
  onChange: (next: DestinationFormState['sqs']) => void
}

export function SqsDestinationForm({ value, onChange }: SqsDestinationFormProps) {
  return (
    <>
      <Text size="2" color="gray">Region</Text>
      <TextField.Root
        placeholder="us-east-1"
        value={value.region}
        onChange={(entry) => onChange({ ...value, region: entry.target.value })}
      />

      <Text size="2" color="gray">Queue URL</Text>
      <TextField.Root
        placeholder="https://sqs.us-east-1.amazonaws.com/123456789012/orders"
        value={value.queueUrl}
        onChange={(entry) => onChange({ ...value, queueUrl: entry.target.value })}
      />

      <Text size="2" color="gray">Service URL (optional)</Text>
      <TextField.Root
        placeholder="http://localhost:4566"
        value={value.serviceUrl}
        onChange={(entry) => onChange({ ...value, serviceUrl: entry.target.value })}
      />

      <Text size="2" color="gray">Timeout seconds</Text>
      <TextField.Root
        placeholder="30"
        value={value.timeoutSeconds}
        onChange={(entry) => onChange({ ...value, timeoutSeconds: entry.target.value })}
      />

      <Text size="2" color="gray">Access key ID</Text>
      <TextField.Root
        placeholder="AKIA..."
        value={value.accessKeyId}
        onChange={(entry) => onChange({ ...value, accessKeyId: entry.target.value })}
      />

      <SecretField
        label="Secret access key"
        value={value.secretAccessKey}
        onChange={(secretAccessKey) => onChange({ ...value, secretAccessKey })}
      />

      <SecretField
        label="Session token"
        value={value.sessionToken}
        onChange={(sessionToken) => onChange({ ...value, sessionToken })}
      />
    </>
  )
}
