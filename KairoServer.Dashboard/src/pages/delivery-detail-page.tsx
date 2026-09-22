import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Link,
  Table,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState } from 'react'
import { NavLink, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useDeliveryAttemptsQuery,
  useDeliveryDetailQuery,
  useDeliveryFailureQuery,
  useRequeueDeliveryMutation,
  useRetryDeliveryMutation,
} from '../features/deliveries/use-deliveries'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DeliveryStatus } from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'

function formatStatus(status: DeliveryStatus) {
  return typeof status === 'string' ? status : String(status)
}

function statusColor(status: DeliveryStatus): 'gray' | 'green' | 'red' | 'amber' {
  const normalized = formatStatus(status).toLowerCase()
  if (normalized === 'delivered') {
    return 'green'
  }

  if (normalized === 'failed' || normalized === 'dead') {
    return 'red'
  }

  return 'amber'
}

function protocolFromMetadata(metadata: Record<string, string>) {
  return metadata.protocol || metadata.Protocol || metadata.transport || 'unknown'
}

function httpStatusFromMetadata(metadata: Record<string, string>) {
  return (
    metadata.httpStatus ||
    metadata.http_status ||
    metadata.statusCode ||
    metadata.status ||
    null
  )
}

export function DeliveryDetailPage() {
  const { id } = useParams()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const canOperateDeliveries = hasPermission(currentUser, 'deliveries.operate')

  const detailQuery = useDeliveryDetailQuery(id)
  const failureQuery = useDeliveryFailureQuery(id)
  const attemptsQuery = useDeliveryAttemptsQuery(id)

  const retryMutation = useRetryDeliveryMutation(id ?? '')
  const requeueMutation = useRequeueDeliveryMutation(id ?? '')

  const [retryReason, setRetryReason] = useState('')
  const [requeueReason, setRequeueReason] = useState('')
  const [operationKey, setOperationKey] = useState('')

  const operationError = useMemo(
    () =>
      getApiErrorMessage(retryMutation.error, '') ||
      getApiErrorMessage(requeueMutation.error, ''),
    [requeueMutation.error, retryMutation.error],
  )

  async function handleRetry() {
    if (!id || !canOperateDeliveries || retryMutation.isPending || !operationKey.trim()) {
      return
    }

    await retryMutation.mutateAsync({
      operationId: operationKey.trim(),
      reason: retryReason.trim() || null,
    })

    setRetryReason('')
  }

  async function handleRequeue() {
    if (!id || !canOperateDeliveries || requeueMutation.isPending || !operationKey.trim()) {
      return
    }

    await requeueMutation.mutateAsync({
      operationId: operationKey.trim(),
      reason: requeueReason.trim() || null,
    })

    setRequeueReason('')
  }

  if (!canReadDeliveries) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Delivery detail</Heading>
          <Text color="gray">You do not have permission to read deliveries.</Text>
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
        <Text color="red">Invalid delivery id.</Text>
      </Card>
    )
  }

  if (detailQuery.isLoading) {
    return (
      <Card>
        <Text size="2" color="gray">Loading delivery detail...</Text>
      </Card>
    )
  }

  if (detailQuery.isError || !detailQuery.data) {
    const forbidden = detailQuery.error instanceof ApiError && detailQuery.error.status === 403
    const notFound = detailQuery.error instanceof ApiError && detailQuery.error.status === 404

    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Delivery detail</Heading>
          <Text color={forbidden || notFound ? 'gray' : 'red'}>
            {forbidden
              ? 'The backend denied access (403).'
              : notFound
                ? 'Delivery not found.'
                : getApiErrorMessage(detailQuery.error, 'Unable to load delivery.')}
          </Text>
          <Button asChild variant="soft">
            <NavLink to="/deliveries">Back to deliveries</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const delivery = detailQuery.data
  const failure = failureQuery.data
  const attempts = attemptsQuery.data ?? []

  return (
    <Flex direction="column" gap="4">
      <Flex align="center" justify="between" wrap="wrap" gap="3">
        <Heading size="6">Delivery detail</Heading>
        <Button asChild variant="soft">
          <NavLink to="/deliveries">Back to list</NavLink>
        </Button>
      </Flex>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Overview</Heading>

          <Text size="2" color="gray">Delivery ID</Text>
          <Text>{delivery.id}</Text>

          <Text size="2" color="gray">Status</Text>
          <Badge color={statusColor(delivery.status)} variant="soft">
            {formatStatus(delivery.status)}
          </Badge>

          <Text size="2" color="gray">Event</Text>
          <Link asChild>
            <NavLink to={`/events/${encodeURIComponent(delivery.eventId)}`}>
              {delivery.eventId}
            </NavLink>
          </Link>

          <Text size="2" color="gray">Customer</Text>
          <Link asChild>
            <NavLink to={`/customers/${encodeURIComponent(delivery.customerId)}`}>
              {delivery.customerId}
            </NavLink>
          </Link>

          <Text size="2" color="gray">Contract</Text>
          <Link asChild>
            <NavLink to={`/contracts/${encodeURIComponent(delivery.contractId)}`}>
              {delivery.contractId}
            </NavLink>
          </Link>

          <Text size="2" color="gray">Destination</Text>
          <Text>{delivery.destinationType}</Text>

          <Text size="2" color="gray">Created time</Text>
          <Text>{new Date(delivery.createdAt).toLocaleString()}</Text>

          <Text size="2" color="gray">Next retry time</Text>
          <Text>{delivery.nextAttemptAt ? new Date(delivery.nextAttemptAt).toLocaleString() : '-'}</Text>

          <Text size="2" color="gray">Latest error</Text>
          <Text>{delivery.lastError ?? '-'}</Text>
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Failure details</Heading>
          {failureQuery.isLoading ? (
            <Text size="2" color="gray">Loading failure details...</Text>
          ) : failureQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(failureQuery.error, 'Unable to load failure details.')}
            </Text>
          ) : !failure ? (
            <Text size="2" color="gray">No failure details available.</Text>
          ) : (
            <>
              <Text size="2" color="gray">Failure category</Text>
              <Text>{failure.failureCategory ?? '-'}</Text>
              <Text size="2" color="gray">Failed at</Text>
              <Text>{failure.failedAt ? new Date(failure.failedAt).toLocaleString() : '-'}</Text>
            </>
          )}
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Delivery attempt timeline</Heading>
          {attemptsQuery.isLoading ? (
            <Text size="2" color="gray">Loading attempts...</Text>
          ) : attemptsQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(attemptsQuery.error, 'Unable to load attempts.')}
            </Text>
          ) : attempts.length === 0 ? (
            <Text size="2" color="gray">No attempts recorded.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>#</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Started</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Finished</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Duration</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Result</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Failure category</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Protocol</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>HTTP status</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Error details</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Retry schedule</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {attempts.map((attempt) => (
                  <Table.Row key={attempt.id}>
                    <Table.RowHeaderCell>{attempt.attemptNumber}</Table.RowHeaderCell>
                    <Table.Cell>{new Date(attempt.startedAt).toLocaleString()}</Table.Cell>
                    <Table.Cell>{new Date(attempt.completedAt).toLocaleString()}</Table.Cell>
                    <Table.Cell>{attempt.durationMs} ms</Table.Cell>
                    <Table.Cell>
                      <Badge color={attempt.succeeded ? 'green' : 'red'} variant="soft">
                        {attempt.succeeded ? 'Success' : 'Failure'}
                      </Badge>
                    </Table.Cell>
                    <Table.Cell>{attempt.failureCategory ?? '-'}</Table.Cell>
                    <Table.Cell>{protocolFromMetadata(attempt.metadata)}</Table.Cell>
                    <Table.Cell>{httpStatusFromMetadata(attempt.metadata) ?? '-'}</Table.Cell>
                    <Table.Cell>{attempt.error ?? '-'}</Table.Cell>
                    <Table.Cell>
                      {attempt.succeeded
                        ? '-'
                        : delivery.nextAttemptAt
                          ? new Date(delivery.nextAttemptAt).toLocaleString()
                          : '-'}
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Operations</Heading>

          {!canOperateDeliveries ? (
            <Text size="2" color="gray">Delivery operations are not available for your role.</Text>
          ) : (
            <>
              <Text size="2" color="gray">Operation ID</Text>
              <TextField.Root
                placeholder="GUID"
                value={operationKey}
                onChange={(event) => setOperationKey(event.target.value)}
                disabled={retryMutation.isPending || requeueMutation.isPending}
              />

              <Text size="2" color="gray">Retry reason</Text>
              <TextArea
                value={retryReason}
                onChange={(event) => setRetryReason(event.target.value)}
                rows={2}
                disabled={retryMutation.isPending || requeueMutation.isPending}
              />

              <Text size="2" color="gray">Redelivery (requeue) reason</Text>
              <TextArea
                value={requeueReason}
                onChange={(event) => setRequeueReason(event.target.value)}
                rows={2}
                disabled={retryMutation.isPending || requeueMutation.isPending}
              />

              {operationError ? (
                <Text size="2" color="red">
                  {operationError}
                </Text>
              ) : null}

              <Flex gap="2" wrap="wrap">
                <Button
                  type="button"
                  disabled={!operationKey.trim() || retryMutation.isPending || requeueMutation.isPending}
                  onClick={() => void handleRetry()}
                >
                  {retryMutation.isPending ? 'Retrying...' : 'Retry'}
                </Button>
                <Button
                  type="button"
                  variant="soft"
                  disabled={!operationKey.trim() || retryMutation.isPending || requeueMutation.isPending}
                  onClick={() => void handleRequeue()}
                >
                  {requeueMutation.isPending ? 'Submitting...' : 'Redelivery (requeue)'}
                </Button>
              </Flex>
            </>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
