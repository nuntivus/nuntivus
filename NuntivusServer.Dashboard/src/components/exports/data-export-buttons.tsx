import { Button, Flex, Text } from '@radix-ui/themes'
import { useEffect, useRef, useState } from 'react'
import { FormErrorText } from '../forms/form-feedback'
import { useAppSelector } from '../../store/hooks'
import { apiDownload, getApiErrorMessage } from '../../services/api/api-client'

type Filters = Record<string, string | number | boolean | undefined>
const filterNames = new Set(['id', 'eventId', 'contractId', 'customerId', 'type', 'source', 'eventType', 'status', 'destinationType', 'failureCategory', 'search', 'enabled', 'createdFrom', 'createdTo', 'occurredFrom', 'occurredTo', 'receivedFrom', 'receivedTo'])
export function DataExportButtons({ entity, filters, disabled = false }: { entity: 'events' | 'deliveries' | 'contracts'; filters: Filters; disabled?: boolean }) {
  const user = useAppSelector(state => state.auth.currentUser)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const controller = useRef<AbortController | null>(null)
  useEffect(() => () => controller.current?.abort(), [])
  async function download(format: 'csv' | 'json') {
    const abort = new AbortController()
    controller.current = abort
    setPending(true); setError(null)
    try {
      const query: Record<string, string | number | boolean> = { format }
      Object.entries(filters).forEach(([key, value]) => { if (filterNames.has(key) && value !== undefined && value !== '') query[key] = value })
      const blob = await apiDownload(`/v1/exports/${entity}`, query, abort.signal)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url; link.download = `${entity}.${format}`; document.body.appendChild(link); link.click(); link.remove()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (cause) { if (!abort.signal.aborted) setError(getApiErrorMessage(cause, 'Unable to export data.')) }
    finally { setPending(false); controller.current = null }
  }
  if (!user?.roles.some(role => role === 'Admin' || role === 'Operator')) return null
  return <Flex direction="column" gap="1"><Flex gap="2" wrap="wrap">
    <Button variant="soft" disabled={pending || disabled} onClick={() => void download('csv')}>Export CSV</Button>
    <Button variant="soft" disabled={pending || disabled} onClick={() => void download('json')}>Export JSON</Button>
    {pending ? <Button variant="soft" onClick={() => controller.current?.abort()}>Cancel export</Button> : null}
  </Flex><Text size="1" color="gray">Metadata only · applied filters · oldest first · maximum 10,000 rows</Text><FormErrorText message={error} /></Flex>
}
