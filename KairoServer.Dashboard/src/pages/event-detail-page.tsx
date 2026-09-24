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
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
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

function formatDeliveryStatus(status: number | string) {
  if (typeof status === 'string') {
    return status
  }

  const map: Record<number, string> = {
    0: 'Pending',
    1: 'InFlight',
    2: 'Delivered',
    3: 'Failed',
    4: 'Dead',
  }

  return map[status] ?? String(status)
}

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
  const replayedDeliveriesQuery = useReplayedEventDeliveriesQuery(lastReplay?.replayedEventId)

  const replayKeyError = !replayKey.trim() ? 'Replay key is required.' : null

  async function handleCopy() {
    const value = detail?.id ?? ''

    if (!value) {
      return
    }

    await navigator.clipboard.writeText(value)
    setCopiedField('id')

    window.setTimeout(() => {
      setCopiedField((current) => (current === 'id' ? null : current))
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

  return (
    <Flex direction="column" gap="4">
      <Flex align="center" justify="between" wrap="wrap" gap="3">
        <Heading size="6">Event detail</Heading>
        <Button asChild variant="soft">
          <NavLink to="/events">Back to list</NavLink>
        </Button>
      </Flex>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Overview</Heading>

          <Text size="2" color="gray">Event ID</Text>
          <Flex align="center" gap="2" wrap="wrap">
            <Text>{detail.id}</Text>
            <Button variant="soft" size="1" onClick={() => void handleCopy()}>
              {copiedField === 'id' ? 'Copied' : 'Copy ID'}
            </Button>
          </Flex>

          <Text size="2" color="gray">Type</Text>
          <Text>{detail.type}</Text>

          <Text size="2" color="gray">Source</Text>
          <Text>{detail.source}</Text>

          <Text size="2" color="gray">Occurred timestamp</Text>
          <Text>{new Date(detail.occurredAt).toLocaleString()}</Text>

          <Text size="2" color="gray">Received timestamp</Text>
          <Text>{new Date(detail.receivedAt).toLocaleString()}</Text>

          <Text size="2" color="gray">Delivery summary</Text>
          <Flex gap="1" wrap="wrap">
            <Badge variant="soft">Total {deliverySummary.total}</Badge>
            <Badge color="green" variant="soft">Delivered {deliverySummary.delivered}</Badge>
            <Badge color="amber" variant="soft">Pending {deliverySummary.pending}</Badge>
            <Badge color="red" variant="soft">Failed/Dead {deliverySummary.failedOrDead}</Badge>
          </Flex>
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Metadata</Heading>
          <JsonViewer
            value={detail.metadata ?? {}}
            maxHeight={280}
            defaultCollapsed
            searchPlaceholder="Search metadata"
            copyLabel="Copy metadata"
          />
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Payload</Heading>
          <JsonViewer
            value={detail.payload ?? {}}
            maxHeight={380}
            searchPlaceholder="Search payload"
            copyLabel="Copy payload"
          />
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Related deliveries</Heading>
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
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {detail.deliveries.map((delivery) => (
                  <Table.Row key={delivery.id}>
                    <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                    <Table.Cell>{delivery.contractId}</Table.Cell>
                    <Table.Cell>{formatDeliveryStatus(delivery.status)}</Table.Cell>
                    <Table.Cell>{delivery.attemptCount}</Table.Cell>
                    <Table.Cell>{new Date(delivery.updatedAt).toLocaleString()}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Replay</Heading>
          <Text size="2" color="gray">
            Replay creates a new event from this source event and re-runs contract matching.
            It can produce new deliveries even if previous deliveries already exist.
          </Text>
          {!canReplayEvents ? (
            <Text size="2" color="gray">
              Replay is not available for your role.
            </Text>
          ) : (
            <>
              <TextField.Root
                placeholder="Replay key"
                value={replayKey}
                onChange={(event) => setReplayKey(event.target.value)}
                disabled={replayMutation.isPending}
                aria-invalid={replayKeyError ? 'true' : 'false'}
                aria-describedby={replayKeyError ? 'event-replay-key-error' : undefined}
              />
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
                            <Table.Cell>{delivery.contractId}</Table.Cell>
                            <Table.Cell>{formatDeliveryStatus(delivery.status)}</Table.Cell>
                            <Table.Cell>{delivery.attemptCount}</Table.Cell>
                          </Table.Row>
                        ))}
                      </Table.Body>
                    </Table.Root>
                  )}
                </>
              ) : null}

              <Button
                type="button"
                onClick={() => void handleReplay()}
                disabled={replayMutation.isPending || !!replayKeyError}
              >
                {replayMutation.isPending ? 'Submitting...' : 'Replay event'}
              </Button>
            </>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
