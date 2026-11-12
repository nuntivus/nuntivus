import { Badge, Button, Card, Flex, Heading, Text, TextArea } from '@radix-ui/themes'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { FieldErrorText, FormErrorText } from '../../components/forms/form-feedback'
import { useConfirmDialog } from '../../hooks/use-confirm-dialog'
import { useNotifications } from '../../hooks/use-notifications'
import { useUnsavedChangesGuard } from '../../hooks/use-unsaved-changes-guard'
import { getApiErrorMessage } from '../../services/api/api-client'
import { activateSchemaVersion, getSchemaCompatibility, type EventSchemaVersion } from '../../services/api/event-definitions-api'
import { SchemaChangeImpactPanel } from './change-impact-panel'

type Props = { eventType: string; schema: EventSchemaVersion; canOverride: boolean }

export function SchemaCompatibilityPanel({ eventType, schema, canOverride }: Props) {
  const queryClient = useQueryClient()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const [reason, setReason] = useState('')
  const [attempted, setAttempted] = useState(false)
  const report = useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'compatibility', schema.version],
    queryFn: ({ signal }) => getSchemaCompatibility(eventType, schema.version, { signal }),
  })
  const activation = useMutation({
    mutationFn: () => activateSchemaVersion(eventType, schema.version, { overrideCompatibility: true, reason: reason.trim() }),
    onSuccess: async () => {
      setReason('')
      setAttempted(false)
      await queryClient.invalidateQueries({ queryKey: ['event-definitions'] })
      notifications.success('Schema activated with an audited compatibility override.', 'Schema compatibility')
    },
  })
  useUnsavedChangesGuard({ when: reason.length > 0 })
  const reasonError = !reason.trim() || reason.length > 2000 ? 'Enter an override reason of at most 2000 characters.' : null

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setAttempted(true)
    if (!canOverride || reasonError || activation.isPending) return
    const accepted = await confirm({
      title: 'Override schema compatibility',
      description: `Activate version ${schema.version} despite compatibility findings? This can reject incoming events or break consumers. Your identity and reason will be recorded.`,
      confirmText: 'Override and activate',
      confirmColor: 'red',
    })
    if (!accepted) return
    try { await activation.mutateAsync() } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to activate schema.'), 'Schema compatibility')
    }
  }

  const blocked = report.data?.status !== 'Compatible'
  return (
    <Card className="detail-panel detail-panel-wide">
      <Flex direction="column" gap="3">
        <Heading size="4">Schema compatibility · v{schema.version}</Heading>
        {report.isPending ? <Text color="gray">Checking compatibility...</Text> : report.isError ? (
          <FormErrorText message={getApiErrorMessage(report.error, 'Unable to compare schemas.')} />
        ) : report.data ? (
          <>
            <Flex gap="2" align="center" wrap="wrap">
              <Badge color={report.data.status === 'Compatible' ? 'green' : report.data.status === 'Breaking' ? 'red' : 'amber'}>{report.data.status}</Badge>
              <Text size="2">{report.data.mode} · {report.data.againstVersion ? `compared with v${report.data.againstVersion}` : 'no active baseline'}</Text>
            </Flex>
            {report.data.findings.length > 0 ? (
              <ul>{report.data.findings.map((finding, index) => <li key={`${finding.path}:${index}`}><code>{finding.path}</code> — {finding.message}</li>)}</ul>
            ) : <Text size="2" color="gray">No incompatible changes detected by the supported comparison rules.</Text>}
            {blocked ? <Text size="2" color="gray">Activation is blocked. Indeterminate constructs require review and are never treated as safe.</Text> : null}
            {blocked && canOverride && schema.state === 'Draft' ? (
              <form onSubmit={submit}>
                <Flex direction="column" gap="2" style={{ maxWidth: 640 }}>
                  <Text as="label" htmlFor="schema-override-reason" weight="medium">Administrator override reason</Text>
                  <TextArea id="schema-override-reason" value={reason} onChange={event => setReason(event.target.value)} maxLength={2000}
                    disabled={activation.isPending} aria-invalid={attempted && !!reasonError} aria-describedby="schema-override-help schema-override-error" />
                  <Text id="schema-override-help" size="1" color="gray">An explicit reason and your authenticated identity are saved with the activation audit record.</Text>
                  <FieldErrorText id="schema-override-error" message={attempted ? reasonError : null} />
                  <FormErrorText message={activation.isError ? getApiErrorMessage(activation.error, 'Unable to activate schema.') : null} />
                  <Flex gap="2">
                    <Button type="submit" color="red" disabled={activation.isPending}>{activation.isPending ? 'Activating...' : 'Override and activate'}</Button>
                    <Button type="button" variant="soft" onClick={() => { setReason(''); setAttempted(false) }} disabled={activation.isPending}>Cancel</Button>
                  </Flex>
                </Flex>
              </form>
            ) : null}
          </>
        ) : null}
        <SchemaChangeImpactPanel key={`${eventType}:${schema.version}`} eventType={eventType} version={schema.version} />
      </Flex>
    </Card>
  )
}
