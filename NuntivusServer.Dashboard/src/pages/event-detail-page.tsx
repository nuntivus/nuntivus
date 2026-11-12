import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Table,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useState } from 'react'
import { NavLink, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { JsonViewer } from '../components/json/json-viewer'
import { EntityReference } from '../components/links/entity-reference'
import { useEntityLabel } from '../components/links/use-entity-label'
import { CalendarIcon, ChevronRightIcon, ClockIcon, CodeIcon, CopyIcon, DeliveriesIcon, EventsIcon, RetryIcon } from '../components/icons/nav-icons'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { DetailMetric } from '../components/states/detail-visuals'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { formatDeliveryStatus } from '../features/deliveries/delivery-status'
import {
  useEventDetailQuery,
  useReplayEventMutation,
  useReplayedEventDeliveriesQuery,
} from '../features/events/use-events'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DeliveryListItem } from '../services/api/events-api'
import { useAppSelector } from '../store/hooks'

function summarizeDeliveries(deliveries: DeliveryListItem[]) {
  let pending = 0
  let failedOrDead = 0
  let delivered = 0

  deliveries.forEach((item) => {
    const normalized = formatDeliveryStatus(item.status).toLowerCase()
    if (normalized === 'pending' || normalized === 'inflight') {
      pending += 1
      return
    }

    if (normalized === 'failed' || normalized === 'dead') {
      failedOrDead += 1
      return
    }

    if (normalized === 'delivered') {
      delivered += 1
    }
  })

  return {
    total: deliveries.length,
    pending,
    failedOrDead,
    delivered,
  }
}

function formatEventDate(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}

function deliveryBadgeColor(status: number | string): 'amber' | 'blue' | 'green' | 'red' | 'gray' {
  const normalized = formatDeliveryStatus(status).toLowerCase()

  if (normalized === 'delivered') return 'green'
  if (normalized === 'pending') return 'amber'
  if (normalized === 'inflight') return 'blue'
  if (normalized === 'failed' || normalized === 'dead') return 'red'
  return 'gray'
}

export function EventDetailPage() {
  const { id } = useParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadEvents = hasPermission(currentUser, 'events.read')
  const canReplayEvents = hasPermission(currentUser, 'events.replay')

  const detailQuery = useEventDetailQuery(id)
  const replayMutation = useReplayEventMutation(id ?? '')

  const [replayKey, setReplayKey] = useState('')
  const [replayReason, setReplayReason] = useState('')
  const [copiedField, setCopiedField] = useState<string | null>(null)
  const [lastReplay, setLastReplay] = useState<{
    sourceEventId: string
    replayedEventId: string
    status: string
  } | null>(null)

  const detail = detailQuery.data
  const sourceName = useEntityLabel('source', detail?.source)
  const replayedDeliveriesQuery = useReplayedEventDeliveriesQuery(lastReplay?.replayedEventId)

  const replayKeyError = !replayKey.trim() ? 'Replay key is required.' : null

  async function handleCopy(value: string, field: string) {
    if (!value) {
      return
    }

    await navigator.clipboard.writeText(value)
    setCopiedField(field)

    window.setTimeout(() => {
      setCopiedField((current) => (current === field ? null : current))
    }, 1500)
  }

  async function handleReplay() {
    if (!id || !canReplayEvents || !!replayKeyError || replayMutation.isPending) {
      return
    }

    const confirmed = await confirm({
      title: 'Replay event',
      description:
        'Replay will create a new event and can trigger new deliveries for matching contracts. This can duplicate downstream effects. Continue?',
      confirmText: 'Replay',
      cancelText: 'Cancel',
      confirmColor: 'amber',
    })

    if (!confirmed) {
      return
    }

    try {
      const replay = await replayMutation.mutateAsync({
        replayKey: replayKey.trim(),
        reason: replayReason.trim() || null,
      })

      setLastReplay(replay)

      setReplayKey('')
      setReplayReason('')
      notifications.success('Event replay started successfully.', 'Replay event')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to replay event.'), 'Replay event')
    }
  }

  if (!canReadEvents) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Event detail</Heading>
          <Text color="gray">You do not have permission to read events.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  if (!id) {
    return (
      <Card>
        <Text color="red">Invalid event id.</Text>
      </Card>
    )
  }

  if (detailQuery.isLoading) {
    return <DetailLoadingState message="Loading event detail..." />
  }

  if (detailQuery.isError || !detail) {
    const forbidden = detailQuery.error instanceof ApiError && detailQuery.error.status === 403
    const notFound = detailQuery.error instanceof ApiError && detailQuery.error.status === 404

    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Event detail</Heading>
          <Text color={forbidden || notFound ? 'gray' : 'red'}>
            {forbidden
              ? 'The backend denied access (403).'
              : notFound
                ? 'Event not found.'
                : getApiErrorMessage(detailQuery.error, 'Unable to load event.')}
          </Text>
          <Button asChild variant="soft">
            <NavLink to="/events">Back to events</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const deliverySummary = summarizeDeliveries(detail.deliveries)

  const eventInformation = JSON.stringify({
    id: detail.id,
    type: detail.type,
    source: detail.source,
    occurredAt: detail.occurredAt,
    receivedAt: detail.receivedAt,
    inputSchemaVersion: detail.inputSchemaVersion,
    schemaValidationStatus: detail.schemaValidationStatus,
  }, null, 2)
  const timelineDeliveries = [...detail.deliveries].sort(
    (left, right) => new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
  )

  return (
    <Flex direction="column" gap="3" className="detail-page event-detail-page">
      <Flex align="center" justify="between" wrap="wrap" gap="3" className="event-detail-header">
        <Flex align="center" gap="3" className="event-detail-heading">
          <span className="detail-page-icon" aria-hidden="true"><EventsIcon /></span>
          <div>
            <Flex align="center" gap="2" wrap="wrap">
              <Heading size="6">{detail.type}</Heading>
              <Badge variant="soft">Event</Badge>
            </Flex>
            <Text size="2" color="gray">Event timing, payload, linked deliveries, and replay controls.</Text>
          </div>
        </Flex>
        <Button asChild variant="soft">
          <NavLink to="/events"><ChevronRightIcon className="event-back-icon" />Back to list</NavLink>
        </Button>
      </Flex>

      <div className="visual-detail-metric-grid">
        <DetailMetric icon={EventsIcon} label="Event type" value={detail.type} supporting={`Source: ${sourceName ?? detail.source}`} />
        <DetailMetric icon={CalendarIcon} label="Occurred" value={formatEventDate(detail.occurredAt)} supporting="Producer timestamp" />
        <DetailMetric icon={ClockIcon} label="Received" value={formatEventDate(detail.receivedAt)} supporting="Ingestion timestamp" tone="violet" />
        <DetailMetric icon={DeliveriesIcon} label="Deliveries" value={deliverySummary.total} supporting={`${deliverySummary.delivered} delivered / ${deliverySummary.failedOrDead} failed`} tone={deliverySummary.failedOrDead ? 'danger' : 'success'} />
      </div>

      <Card className="detail-panel detail-panel-wide event-detail-card event-timeline-card">
        <div className="event-section-header">
          <Flex align="center" gap="2">
            <span className="event-section-icon"><ClockIcon /></span>
            <div><Heading size="4">Delivery timeline</Heading><Text size="1" color="gray">Event ingestion and delivery activity in chronological order.</Text></div>
          </Flex>
        </div>
        <div className="event-timeline-scroll">
          <ol className="event-timeline">
            <li className="event-timeline-item event-timeline-received">
              <span className="event-timeline-marker"><EventsIcon /></span>
              <div className="event-timeline-content">
                <Text size="1" color="gray">Event received</Text>
                <Text size="2" weight="bold">{formatEventDate(detail.receivedAt)}</Text>
                <Text size="1" color="gray">Ready for contract matching</Text>
              </div>
            </li>
            {timelineDeliveries.map((delivery) => (
              <li className={`event-timeline-item event-timeline-${deliveryBadgeColor(delivery.status)}`} key={delivery.id}>
                <span className="event-timeline-marker"><DeliveriesIcon /></span>
                <div className="event-timeline-content">
                  <Flex align="center" gap="1" wrap="wrap">
                    <Text size="1" color="gray">Delivery</Text>
                    <Badge color={deliveryBadgeColor(delivery.status)} variant="soft">{formatDeliveryStatus(delivery.status)}</Badge>
                  </Flex>
                   <NavLink className="event-timeline-link" to={`/deliveries/${delivery.id}`} title={`Delivery ID: ${delivery.id}`}>
                     {delivery.eventType} · {delivery.id.slice(0, 8)}
                   </NavLink>
                  <Text size="1" color="gray">{formatEventDate(delivery.updatedAt)} / {delivery.attemptCount} attempt{delivery.attemptCount === 1 ? '' : 's'}</Text>
                </div>
              </li>
            ))}
          </ol>
        </div>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-information-card">
        <div className="event-section-header">
          <Flex align="center" gap="2">
            <span className="event-section-icon"><EventsIcon /></span>
            <div><Heading size="4">Event information</Heading><Text size="1" color="gray">Detailed information about this event.</Text></div>
          </Flex>
          <Button variant="soft" size="1" onClick={() => void handleCopy(eventInformation, 'all')}><CopyIcon />{copiedField === 'all' ? 'Copied' : 'Copy all'}</Button>
        </div>
        <div className="event-information-list">
          <div className="event-information-row"><EventsIcon /><Text weight="medium">Event ID</Text><Text>{detail.id}</Text><Button variant="ghost" size="1" aria-label="Copy event ID" onClick={() => void handleCopy(detail.id, 'id')}><CopyIcon /></Button></div>
          <div className="event-information-row"><EventsIcon /><Text weight="medium">Event type</Text><Text>{detail.type}</Text><Button variant="ghost" size="1" aria-label="Copy event type" onClick={() => void handleCopy(detail.type, 'type')}><CopyIcon /></Button></div>
          <div className="event-information-row"><CodeIcon /><Text weight="medium">Source</Text><Text><EntityReference kind="source" id={detail.source} /></Text><Button variant="ghost" size="1" aria-label="Copy source" onClick={() => void handleCopy(detail.source, 'source')}><CopyIcon /></Button></div>
          <div className="event-information-row"><CodeIcon /><Text weight="medium">Input schema</Text><Text>{detail.inputSchemaVersion ? `Version ${detail.inputSchemaVersion}` : 'None recorded'}</Text><span /></div>
          <div className="event-information-row"><CodeIcon /><Text weight="medium">Schema validation</Text><Text>{detail.schemaValidationStatus === 'Valid' ? 'Valid' : detail.schemaValidationStatus === 'NotConfigured' ? 'No active schema at ingestion' : 'Not recorded'}</Text><span /></div>
          <div className="event-information-row"><CalendarIcon /><Text weight="medium">Occurred timestamp</Text><Text>{formatEventDate(detail.occurredAt)}</Text><Button variant="ghost" size="1" aria-label="Copy occurred timestamp" onClick={() => void handleCopy(detail.occurredAt, 'occurred')}><CopyIcon /></Button></div>
          <div className="event-information-row"><ClockIcon /><Text weight="medium">Received timestamp</Text><Text>{formatEventDate(detail.receivedAt)}</Text><Button variant="ghost" size="1" aria-label="Copy received timestamp" onClick={() => void handleCopy(detail.receivedAt, 'received')}><CopyIcon /></Button></div>
          <div className="event-information-row"><DeliveriesIcon /><Text weight="medium">Delivery summary</Text><Flex gap="1" wrap="wrap"><Badge variant="soft">Total {deliverySummary.total}</Badge><Badge color="green" variant="soft">Delivered {deliverySummary.delivered}</Badge><Badge color="amber" variant="soft">Pending {deliverySummary.pending}</Badge><Badge color="red" variant="soft">Failed/Dead {deliverySummary.failedOrDead}</Badge></Flex><span /></div>
        </div>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-json-card">
        <Flex direction="column" gap="3">
          <div className="event-section-header"><Flex align="center" gap="2"><span className="event-section-icon event-section-icon-violet"><CodeIcon /></span><div><Heading size="4">Metadata</Heading><Text size="1" color="gray">Additional metadata included with this event.</Text></div></Flex></div>
          <JsonViewer
            value={detail.metadata ?? {}}
            maxHeight={280}
            searchPlaceholder="Search metadata"
            copyLabel="Copy metadata"
          />
        </Flex>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-json-card">
        <Flex direction="column" gap="3">
          <div className="event-section-header"><Flex align="center" gap="2"><span className="event-section-icon"><CodeIcon /></span><div><Heading size="4">Payload</Heading><Text size="1" color="gray">The raw event payload as received.</Text></div></Flex></div>
          <JsonViewer
            value={detail.payload ?? {}}
            maxHeight={380}
            searchPlaceholder="Search payload"
            copyLabel="Copy payload"
          />
        </Flex>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-deliveries-card">
        <Flex direction="column" gap="3">
          <div className="event-section-header"><Flex align="center" gap="2"><span className="event-section-icon"><DeliveriesIcon /></span><div><Heading size="4">Related deliveries</Heading><Text size="1" color="gray">Deliveries created for this event.</Text></div></Flex></div>
          {detail.deliveries.length === 0 ? (
            <Text size="2" color="gray">No deliveries linked to this event.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Updated</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell aria-label="Actions" />
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {detail.deliveries.map((delivery) => (
                  <Table.Row key={delivery.id}>
                    <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                     <Table.Cell><EntityReference kind="contract" id={delivery.contractId} /></Table.Cell>
                    <Table.Cell><Badge color={deliveryBadgeColor(delivery.status)} variant="soft">{formatDeliveryStatus(delivery.status)}</Badge></Table.Cell>
                    <Table.Cell>{delivery.attemptCount}</Table.Cell>
                    <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                    <Table.Cell><Button asChild variant="ghost" size="1" aria-label={`Open delivery ${delivery.id}`}><NavLink to={`/deliveries/${delivery.id}`}><ChevronRightIcon /></NavLink></Button></Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Card className="detail-panel detail-panel-wide event-detail-card event-replay-card">
        <Flex direction="column" gap="3">
          <div className="event-section-header"><Flex align="center" gap="2"><span className="event-section-icon"><RetryIcon /></span><div><Heading size="4">Replay event</Heading><Text size="1" color="gray">Create a new event from this event and re-run contract matching. This can produce new deliveries even if previous deliveries already exist.</Text></div></Flex></div>
          {!canReplayEvents ? (
            <Text size="2" color="gray">
              Replay is not available for your role.
            </Text>
          ) : (
            <>
              <div className="event-replay-controls">
                <TextField.Root placeholder="Replay key" value={replayKey} onChange={(event) => setReplayKey(event.target.value)} disabled={replayMutation.isPending} aria-invalid={replayKeyError ? 'true' : 'false'} aria-describedby={replayKeyError ? 'event-replay-key-error' : undefined} />
                <Button type="button" onClick={() => void handleReplay()} disabled={replayMutation.isPending || !!replayKeyError}><RetryIcon />{replayMutation.isPending ? 'Submitting...' : 'Replay event'}</Button>
              </div>
              <FieldErrorText id="event-replay-key-error" message={replayKeyError} />
              <TextArea
                placeholder="Reason (optional)"
                value={replayReason}
                onChange={(event) => setReplayReason(event.target.value)}
                rows={3}
                disabled={replayMutation.isPending}
              />

              <FormErrorText
                id="event-replay-submit-error"
                message={
                  replayMutation.isError
                    ? getApiErrorMessage(replayMutation.error, 'Unable to replay event.')
                    : null
                }
              />

              {replayMutation.isSuccess ? (
                <Text size="2" color="green">
                  Replay accepted: {replayMutation.data.replayedEventId}
                </Text>
              ) : null}

              {lastReplay ? (
                <Flex direction="column" gap="1">
                  <Text size="2" color="gray">Replay history (current session)</Text>
                  <Text size="2">Source: {lastReplay.sourceEventId}</Text>
                  <Text size="2">Replayed event: {lastReplay.replayedEventId}</Text>
                  <Text size="2">Status: {lastReplay.status}</Text>
                </Flex>
              ) : (
                <Text size="2" color="gray">
                  Replay history is not exposed by the backend yet.
                </Text>
              )}

              {lastReplay ? (
                <>
                  <Heading size="3">Newly created deliveries</Heading>
                  {replayedDeliveriesQuery.isLoading ? (
                    <Text size="2" color="gray">Loading replayed event deliveries...</Text>
                  ) : replayedDeliveriesQuery.isError ? (
                    <Text size="2" color="red">
                      {getApiErrorMessage(
                        replayedDeliveriesQuery.error,
                        'Unable to load replayed deliveries.',
                      )}
                    </Text>
                  ) : (replayedDeliveriesQuery.data ?? []).length === 0 ? (
                    <Text size="2" color="gray">
                      No deliveries were created for the replayed event.
                    </Text>
                  ) : (
                    <Table.Root>
                      <Table.Header>
                        <Table.Row>
                          <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                          <Table.ColumnHeaderCell>Contract</Table.ColumnHeaderCell>
                          <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                          <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                        </Table.Row>
                      </Table.Header>
                      <Table.Body>
                        {(replayedDeliveriesQuery.data ?? []).map((delivery) => (
                          <Table.Row key={delivery.id}>
                            <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                            <Table.Cell><EntityReference kind="contract" id={delivery.contractId} /></Table.Cell>
                            <Table.Cell>{formatDeliveryStatus(delivery.status)}</Table.Cell>
                            <Table.Cell>{delivery.attemptCount}</Table.Cell>
                          </Table.Row>
                        ))}
                      </Table.Body>
                    </Table.Root>
                  )}
                </>
              ) : null}

            </>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
