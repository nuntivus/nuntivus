import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  IconButton,
  Table,
  Tabs,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type ComponentType, type ReactNode, type SVGProps } from 'react'
import { NavLink, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { ErrorPreview } from '../components/errors/error-preview'
import { JsonViewer } from '../components/json/json-viewer'
import { EntityReference } from '../components/links/entity-reference'
import {
  AlertIcon,
  CalendarIcon,
  CheckCircleIcon,
  ClockIcon,
  CodeIcon,
  ContractsIcon,
  CopyIcon,
  CustomersIcon,
  DeliveriesIcon,
  DestinationsIcon,
  EventsIcon,
  FailedDeliveriesIcon,
  RetryIcon,
} from '../components/icons/nav-icons'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import {
  useDeliveryAttemptsQuery,
  useDeliveryDetailQuery,
  useDeliveryFailureQuery,
  useRequeueDeliveryMutation,
  useRetryDeliveryMutation,
} from '../features/deliveries/use-deliveries'
import { formatDeliveryStatus } from '../features/deliveries/delivery-status'
import { useEventDetailQuery } from '../features/events/use-events'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { useFormValidation } from '../hooks/use-form-validation'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import type { DeliveryStatus } from '../services/api/deliveries-api'
import { useAppSelector } from '../store/hooks'

function statusColor(status: DeliveryStatus): 'gray' | 'green' | 'red' | 'amber' {
  const normalized = formatDeliveryStatus(status).toLowerCase()
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

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

type DetailIcon = ComponentType<SVGProps<SVGSVGElement>>

function DeliveryMetric({
  icon: Icon,
  label,
  value,
  supporting,
  tone = 'accent',
  copyValue,
  onCopy,
}: {
  icon: DetailIcon
  label: string
  value: ReactNode
  supporting?: string
  tone?: 'accent' | 'danger' | 'success'
  copyValue?: string
  onCopy?: (value: string, label: string) => void
}) {
  return (
    <Card className={`delivery-metric delivery-tone-${tone}`}>
      <span className="delivery-metric-icon"><Icon /></span>
      <div className="delivery-metric-content">
        <Text size="1" color="gray">{label}</Text>
        <div className="delivery-metric-value">{value}</div>
        {supporting ? <Text size="1" color="gray">{supporting}</Text> : null}
      </div>
      {copyValue && onCopy ? (
        <IconButton
          size="1"
          variant="ghost"
          color="gray"
          aria-label={`Copy ${label}`}
          title={`Copy ${label}`}
          onClick={() => onCopy(copyValue, label)}
        >
          <CopyIcon />
        </IconButton>
      ) : null}
    </Card>
  )
}

function DeliverySectionTitle({ icon: Icon, children, tone = 'accent' }: {
  icon: DetailIcon
  children: ReactNode
  tone?: 'accent' | 'danger'
}) {
  return (
    <Flex align="center" gap="2" className={`delivery-section-title delivery-tone-${tone}`}>
      <Icon />
      <Heading size="4">{children}</Heading>
    </Flex>
  )
}

function DeliveryInfoRow({ icon: Icon, label, children, copyValue, onCopy }: {
  icon: DetailIcon
  label: string
  children: ReactNode
  copyValue?: string
  onCopy?: (value: string, label: string) => void
}) {
  return (
    <div className="delivery-info-row">
      <Icon />
      <Text size="2" color="gray" weight="medium">{label}</Text>
      <div className="delivery-info-value">{children}</div>
      {copyValue && onCopy ? (
        <IconButton
          size="1"
          variant="ghost"
          color="gray"
          aria-label={`Copy ${label}`}
          title={`Copy ${label}`}
          onClick={() => onCopy(copyValue, label)}
        >
          <CopyIcon />
        </IconButton>
      ) : null}
    </div>
  )
}

export function DeliveryDetailPage() {
  const { id } = useParams()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const canOperateDeliveries = hasPermission(currentUser, 'deliveries.operate')

  const detailQuery = useDeliveryDetailQuery(id)
  const failureQuery = useDeliveryFailureQuery(id)
  const attemptsQuery = useDeliveryAttemptsQuery(id)
  const eventQuery = useEventDetailQuery(detailQuery.data?.eventId)

  const retryMutation = useRetryDeliveryMutation(id ?? '')
  const requeueMutation = useRequeueDeliveryMutation(id ?? '')

  const [retryReason, setRetryReason] = useState('')
  const [requeueReason, setRequeueReason] = useState('')
  const [operationKey, setOperationKey] = useState<string>(() => crypto.randomUUID())
  const [copiedField, setCopiedField] = useState<string | null>(null)
  const operationValidation = useFormValidation({
    operationKeyError: !operationKey.trim() ? 'Operation ID is required.'
      : !guidPattern.test(operationKey.trim()) ? 'Operation ID must be a valid GUID.' : null,
  })
  const { operationKeyError } = operationValidation.fieldErrors

  const operationError = useMemo(
    () =>
      getApiErrorMessage(retryMutation.error, '') ||
      getApiErrorMessage(requeueMutation.error, ''),
    [requeueMutation.error, retryMutation.error],
  )

  async function handleRetry() {
    if (!id || !canOperateDeliveries || retryMutation.isPending || requeueMutation.isPending || !operationValidation.validate()) {
      return
    }

    const confirmed = await confirm({
      title: 'Retry delivery',
      description:
        'Retry this delivery now? This may trigger immediate redelivery attempts to the destination.',
      confirmText: 'Retry now',
      cancelText: 'Cancel',
      confirmColor: 'amber',
    })

    if (!confirmed) {
      return
    }

    try {
      await retryMutation.mutateAsync({
        operationId: operationKey.trim(),
        reason: retryReason.trim() || null,
      })

      setRetryReason('')
      setOperationKey(crypto.randomUUID())
      operationValidation.reset()
      notifications.success('Delivery retry requested.', 'Delivery operation')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to retry delivery.'), 'Delivery operation')
    }
  }

  async function handleRequeue() {
    if (!id || !canOperateDeliveries || retryMutation.isPending || requeueMutation.isPending || !operationValidation.validate()) {
      return
    }

    const confirmed = await confirm({
      title: 'Requeue delivery',
      description:
        'Requeue this delivery? A new delivery attempt may be created and processed shortly.',
      confirmText: 'Requeue',
      cancelText: 'Cancel',
      confirmColor: 'amber',
    })

    if (!confirmed) {
      return
    }

    try {
      await requeueMutation.mutateAsync({
        operationId: operationKey.trim(),
        reason: requeueReason.trim() || null,
      })

      setRequeueReason('')
      setOperationKey(crypto.randomUUID())
      operationValidation.reset()
      notifications.success('Delivery requeue requested.', 'Delivery operation')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to requeue delivery.'), 'Delivery operation')
    }
  }

  async function handleCopy(value: string, label: string) {
    await navigator.clipboard.writeText(value)
    setCopiedField(label)
    window.setTimeout(() => setCopiedField((current) => current === label ? null : current), 1500)
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
    return <DetailLoadingState message="Loading delivery detail..." />
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
  const event = eventQuery.data
  const normalizedStatus = formatDeliveryStatus(delivery.status).toLowerCase()
  const failed = normalizedStatus === 'failed' || normalizedStatus === 'dead'
  const delivered = normalizedStatus === 'delivered'
  const inFlight = normalizedStatus === 'inflight'
  const attemptCount = Math.max(delivery.attemptCount, attempts.length)
  const failedStepIndex = failed ? (attemptCount > 0 ? 2 : 0) : -1
  const statusTone = failed ? 'danger' : delivered ? 'success' : 'accent'
  const statusDescription = normalizedStatus === 'dead'
    ? 'Delivery failed permanently'
    : failed
      ? 'Delivery attempt failed'
      : delivered
        ? 'Delivery completed successfully'
        : inFlight
          ? 'Delivery is being processed'
          : 'Delivery is waiting to be processed'

  return (
    <Flex direction="column" gap="4" className="delivery-detail-page">
      <Flex align="center" justify="between" wrap="wrap" gap="3" className="delivery-detail-header">
        <Flex align="center" gap="3">
          <span className="delivery-header-icon"><DeliveriesIcon /></span>
          <div>
            <Flex align="center" gap="2" wrap="wrap">
              <Heading size="6">Delivery detail</Heading>
              <Badge color={statusColor(delivery.status)} variant="soft">
                {formatDeliveryStatus(delivery.status)}
              </Badge>
            </Flex>
            <Text size="2" color="gray">Complete information about this delivery and its related data.</Text>
          </div>
        </Flex>
        <div>
          <Button asChild variant="soft">
            <NavLink to="/deliveries">Back to list</NavLink>
          </Button>
        </div>
      </Flex>

      <div className="delivery-metric-grid">
        <DeliveryMetric icon={DeliveriesIcon} label="Delivery ID" value={delivery.id} copyValue={delivery.id} onCopy={handleCopy} />
        <DeliveryMetric
          icon={failed ? FailedDeliveriesIcon : CheckCircleIcon}
          label="Status"
          value={<Text color={failed ? 'red' : delivered ? 'green' : undefined} weight="bold">{formatDeliveryStatus(delivery.status)}</Text>}
          supporting={statusDescription}
          tone={statusTone}
        />
        <DeliveryMetric icon={CalendarIcon} label="Created time" value={new Date(delivery.createdAt).toLocaleString()} />
        <DeliveryMetric
          icon={ClockIcon}
          label="Next retry time"
          value={delivery.nextAttemptAt ? new Date(delivery.nextAttemptAt).toLocaleString() : '-'}
          supporting={delivery.nextAttemptAt ? 'Retry scheduled' : 'No retry scheduled'}
        />
      </div>

      <div className="delivery-primary-grid">
        <Card className="delivery-section-card">
          <DeliverySectionTitle icon={DeliveriesIcon}>Delivery information</DeliverySectionTitle>
          <div className="delivery-info-list">
            <DeliveryInfoRow icon={EventsIcon} label="Event" copyValue={delivery.eventId} onCopy={handleCopy}>
              <EntityReference kind="event" id={delivery.eventId} label={delivery.eventType} />
            </DeliveryInfoRow>
            <DeliveryInfoRow icon={CustomersIcon} label="Customer" copyValue={delivery.customerId} onCopy={handleCopy}>
              <EntityReference kind="customer" id={delivery.customerId} />
            </DeliveryInfoRow>
            <DeliveryInfoRow icon={ContractsIcon} label="Contract" copyValue={delivery.contractId} onCopy={handleCopy}>
              <EntityReference kind="contract" id={delivery.contractId} />
            </DeliveryInfoRow>
            <DeliveryInfoRow icon={DestinationsIcon} label="Destination" copyValue={delivery.destinationId ?? undefined} onCopy={handleCopy}>
              {delivery.destinationId
                ? <EntityReference kind="destination" id={delivery.destinationId} />
                : delivery.destinationType}
            </DeliveryInfoRow>
          </div>
        </Card>

        <Card className="delivery-section-card">
          <DeliverySectionTitle icon={EventsIcon}>Event details</DeliverySectionTitle>
          <div className="delivery-info-list">
            <DeliveryInfoRow icon={EventsIcon} label="Event ID" copyValue={delivery.eventId} onCopy={handleCopy}>{delivery.eventId}</DeliveryInfoRow>
            <DeliveryInfoRow icon={CodeIcon} label="Event type"><Badge variant="soft">{delivery.eventType}</Badge></DeliveryInfoRow>
            <DeliveryInfoRow icon={ContractsIcon} label="Source"><EntityReference kind="source" id={event?.source} /></DeliveryInfoRow>
          </div>
        </Card>

        <Card className="delivery-section-card">
          <DeliverySectionTitle icon={failed ? FailedDeliveriesIcon : CheckCircleIcon} tone={failed ? 'danger' : 'accent'}>Delivery status</DeliverySectionTitle>
          <div className={`delivery-status-track delivery-status-${normalizedStatus}`}>
            {[
              ['Created', new Date(delivery.createdAt).toLocaleString(), true],
              ['Processing', attemptCount ? `${attemptCount} attempt${attemptCount === 1 ? '' : 's'}` : 'Waiting', inFlight || delivered || attemptCount > 0],
              ['Delivered', delivered ? new Date(delivery.updatedAt).toLocaleString() : normalizedStatus === 'dead' || failed && attemptCount > 0 ? 'Not delivered' : 'Pending', delivered],
              ['Completed', delivered ? 'Done' : normalizedStatus === 'dead' ? 'Not completed' : 'Pending', delivered],
            ].map(([label, detail, active], index) => (
              <div className={`delivery-status-step ${active ? 'active' : ''} ${index === failedStepIndex ? 'failed' : ''}`} key={String(label)}>
                <span className="delivery-status-dot">{index === failedStepIndex ? '×' : active ? '✓' : index + 1}</span>
                <Text size="2" weight="medium">{label}</Text>
                <Text size="1" color="gray">{detail}</Text>
              </div>
            ))}
          </div>
        </Card>

        <Card className="delivery-section-card">
          <DeliverySectionTitle icon={AlertIcon} tone={failed ? 'danger' : 'accent'}>Error information</DeliverySectionTitle>
          {failed ? (
            <div className="delivery-error-callout">
              <FailedDeliveriesIcon />
              <div>
                <Text size="2" weight="bold">{normalizedStatus === 'dead' ? 'Delivery marked as dead' : 'Delivery failed'}</Text>
                <Text size="1">{statusDescription} and {delivery.nextAttemptAt ? 'will be retried.' : 'will not be retried automatically.'}</Text>
              </div>
            </div>
          ) : (
            <div className="delivery-success-callout">
              <CheckCircleIcon />
              <Text size="2">No delivery errors were reported.</Text>
            </div>
          )}
          <DeliveryInfoRow icon={CodeIcon} label="Latest error" copyValue={delivery.lastError ?? undefined} onCopy={handleCopy}>
            <ErrorPreview error={delivery.lastError} title="Latest delivery error" />
          </DeliveryInfoRow>
          {failure?.failureCategory ? (
            <DeliveryInfoRow icon={AlertIcon} label="Category">{failure.failureCategory}</DeliveryInfoRow>
          ) : null}
        </Card>
      </div>

      <Card className="delivery-raw-card">
        <DeliverySectionTitle icon={CodeIcon}>Raw data</DeliverySectionTitle>
        <Tabs.Root defaultValue="overview">
          <Tabs.List>
            <Tabs.Trigger value="overview">Overview</Tabs.Trigger>
            <Tabs.Trigger value="payload">Event payload</Tabs.Trigger>
            <Tabs.Trigger value="metadata">Metadata</Tabs.Trigger>
            <Tabs.Trigger value="failure">Failure</Tabs.Trigger>
          </Tabs.List>
          <Tabs.Content value="overview" className="delivery-tab-content">
            <div className="delivery-raw-grid">
              <div className="delivery-info-list">
                <DeliveryInfoRow icon={DeliveriesIcon} label="Delivery ID" copyValue={delivery.id} onCopy={handleCopy}>{delivery.id}</DeliveryInfoRow>
                <DeliveryInfoRow icon={EventsIcon} label="Event ID" copyValue={delivery.eventId} onCopy={handleCopy}>{delivery.eventId}</DeliveryInfoRow>
                <DeliveryInfoRow icon={CustomersIcon} label="Customer"><EntityReference kind="customer" id={delivery.customerId} /></DeliveryInfoRow>
                <DeliveryInfoRow icon={ContractsIcon} label="Contract"><EntityReference kind="contract" id={delivery.contractId} /></DeliveryInfoRow>
                <DeliveryInfoRow icon={failed ? FailedDeliveriesIcon : CheckCircleIcon} label="Status">
                  <Badge color={statusColor(delivery.status)} variant="soft">{formatDeliveryStatus(delivery.status)}</Badge>
                </DeliveryInfoRow>
              </div>
              <div className="delivery-help-callout">
                <ClockIcon />
                <div>
                  <Text size="2" weight="bold">{failed ? 'Need to retry this delivery?' : 'Delivery processing'}</Text>
                  <Text size="1" color="gray">
                    {failed
                      ? 'Use the delivery operations below to retry or requeue this delivery.'
                      : 'Review the latest status and attempt history below.'}
                  </Text>
                </div>
              </div>
            </div>
          </Tabs.Content>
          <Tabs.Content value="payload" className="delivery-tab-content">
            <JsonViewer value={event?.payload ?? { message: 'Event payload is unavailable.' }} maxHeight={360} searchPlaceholder="Search payload" copyLabel="Copy payload" />
          </Tabs.Content>
          <Tabs.Content value="metadata" className="delivery-tab-content">
            <JsonViewer value={event?.metadata ?? attempts.at(-1)?.metadata ?? {}} maxHeight={320} searchPlaceholder="Search metadata" copyLabel="Copy metadata" />
          </Tabs.Content>
          <Tabs.Content value="failure" className="delivery-tab-content">
            <JsonViewer value={failure ?? { message: 'No failure data available.' }} maxHeight={320} searchPlaceholder="Search failure" copyLabel="Copy failure data" />
          </Tabs.Content>
        </Tabs.Root>
      </Card>

      {copiedField ? <Text size="1" color="gray" className="delivery-copy-feedback">{copiedField} copied</Text> : null}

      <Card className="delivery-lower-card">
        <Flex direction="column" gap="3">
          <DeliverySectionTitle icon={ClockIcon}>Delivery attempts</DeliverySectionTitle>
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
                    <Table.Cell><ErrorPreview error={attempt.error} title={`Attempt ${attempt.attemptNumber} error`} /></Table.Cell>
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

      <Card className="delivery-lower-card">
        <Flex direction="column" gap="3">
          <DeliverySectionTitle icon={RetryIcon}>Operations</DeliverySectionTitle>

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
                aria-invalid={!!operationKeyError}
                aria-describedby={operationKeyError ? 'operation-id-error' : undefined}
              />
              {operationKeyError ? <Text id="operation-id-error" size="2" color="red">{operationKeyError}</Text> : null}

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
                  disabled={retryMutation.isPending || requeueMutation.isPending}
                  onClick={() => void handleRetry()}
                >
                  {retryMutation.isPending ? 'Retrying...' : 'Retry'}
                </Button>
                <Button
                  type="button"
                  variant="soft"
                  disabled={retryMutation.isPending || requeueMutation.isPending}
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
