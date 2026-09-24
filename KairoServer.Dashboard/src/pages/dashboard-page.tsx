import { Card, Flex, Heading, Separator, Text } from '@radix-ui/themes'
import {
  useDashboardOverviewQuery,
  useRecentEventsQuery,
  useRecentFailuresQuery,
  useSuccessfulDeliveriesCountQuery,
} from '../features/dashboard/use-dashboard-data'
import { getApiErrorMessage } from '../services/api/api-client'

export function DashboardPage() {
  const overviewQuery = useDashboardOverviewQuery()
  const successfulQuery = useSuccessfulDeliveriesCountQuery()
  const recentEventsQuery = useRecentEventsQuery()
  const recentFailuresQuery = useRecentFailuresQuery()

  const overview = overviewQuery.data
  const recentEvents = recentEventsQuery.data ?? []
  const recentFailures = recentFailuresQuery.data ?? []

  const successfulDeliveries = successfulQuery.data
  const failedDeliveries = overview?.totalFailedDeliveries
  const deadDeliveries = overview?.totalDeadDeliveries
  const pendingDeliveries = overview?.totalPendingDeliveries

  const trackedDeliveriesTotal =
    successfulDeliveries !== undefined &&
    failedDeliveries !== undefined &&
    deadDeliveries !== undefined &&
    pendingDeliveries !== undefined
      ? successfulDeliveries + failedDeliveries + deadDeliveries + pendingDeliveries
      : undefined

  const failedOrDeadDeliveries =
    failedDeliveries !== undefined && deadDeliveries !== undefined
      ? failedDeliveries + deadDeliveries
      : undefined

  const ratesLoading = successfulQuery.isLoading || overviewQuery.isLoading
  const ratesError = successfulQuery.isError
    ? getApiErrorMessage(successfulQuery.error, 'Unable to load delivery rates.')
    : overviewQuery.isError
      ? getApiErrorMessage(overviewQuery.error, 'Unable to load delivery rates.')
      : undefined

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Overview</Heading>

      <Flex gap="3" wrap="wrap">
        <MetricCard
          title="Successful deliveries"
          value={successfulQuery.data?.toString()}
          loading={successfulQuery.isLoading}
          error={getApiErrorMessage(
            successfulQuery.error,
            'Unable to load successful deliveries.',
          )}
        />

        <MetricCard
          title="Failed deliveries"
          value={overview?.totalFailedDeliveries.toString()}
          loading={overviewQuery.isLoading}
          error={getApiErrorMessage(
            overviewQuery.error,
            'Unable to load failed deliveries.',
          )}
        />

        <MetricCard
          title="Dead deliveries"
          value={overview?.totalDeadDeliveries.toString()}
          loading={overviewQuery.isLoading}
          error={getApiErrorMessage(
            overviewQuery.error,
            'Unable to load dead deliveries.',
          )}
        />

        <MetricCard
          title="Delivery success rate"
          value={formatRatePercentage(successfulDeliveries, trackedDeliveriesTotal)}
          loading={ratesLoading}
          error={ratesError}
        />

        <MetricCard
          title="Delivery failure rate"
          value={formatRatePercentage(failedOrDeadDeliveries, trackedDeliveriesTotal)}
          loading={ratesLoading}
          error={ratesError}
        />
      </Flex>

      <div className="responsive-panel-grid">
        <Card style={{ flex: '1 1 420px' }}>
          <Flex direction="column" gap="3">
            <Heading size="4">Recent events</Heading>
            <Separator size="4" />

            {recentEventsQuery.isLoading ? (
              <Text size="2" color="gray">
                Loading recent events...
              </Text>
            ) : recentEventsQuery.isError ? (
              <Text size="2" color="red">
                {getApiErrorMessage(recentEventsQuery.error, 'Unable to load recent events.')}
              </Text>
            ) : recentEvents.length === 0 ? (
              <Text size="2" color="gray">
                No events yet.
              </Text>
            ) : (
              <Flex direction="column" gap="2">
                {recentEvents.map((item) => (
                  <Card key={item.id} variant="surface" size="1">
                    <Flex direction="column" gap="1">
                      <Text size="2" weight="medium">
                        {item.type}
                      </Text>
                      <Text size="1" color="gray">
                        Source: {item.source}
                      </Text>
                      <Text size="1" color="gray">
                        Received: {new Date(item.receivedAt).toLocaleString()}
                      </Text>
                    </Flex>
                  </Card>
                ))}
              </Flex>
            )}
          </Flex>
        </Card>

        <Card style={{ flex: '1 1 420px' }}>
          <Flex direction="column" gap="3">
            <Heading size="4">Recent failures</Heading>
            <Separator size="4" />

            {recentFailuresQuery.isLoading ? (
              <Text size="2" color="gray">
                Loading recent failures...
              </Text>
            ) : recentFailuresQuery.isError ? (
              <Text size="2" color="red">
                {getApiErrorMessage(
                  recentFailuresQuery.error,
                  'Unable to load recent failures.',
                )}
              </Text>
            ) : recentFailures.length === 0 ? (
              <Text size="2" color="gray">
                No failures found.
              </Text>
            ) : (
              <Flex direction="column" gap="2">
                {recentFailures.map((item) => (
                  <Card key={item.deliveryId} variant="surface" size="1">
                    <Flex direction="column" gap="1">
                      <Text size="2" weight="medium">
                        {item.eventType}
                      </Text>
                      <Text size="1" color="gray">
                        Customer: {item.customerId}
                      </Text>
                      <Text size="1" color="gray">
                        Attempts: {item.attemptCount}
                      </Text>
                      <Text size="1" color="gray">
                        Error: {item.lastError ?? 'n/a'}
                      </Text>
                    </Flex>
                  </Card>
                ))}
              </Flex>
            )}
          </Flex>
        </Card>
      </div>
    </Flex>
  )
}

function formatRatePercentage(numerator?: number, denominator?: number) {
  if (numerator === undefined || denominator === undefined || denominator <= 0) {
    return '0.0%'
  }

  return `${((numerator / denominator) * 100).toFixed(1)}%`
}

interface MetricCardProps {
  title: string
  value?: string
  loading?: boolean
  error?: string
  unavailableMessage?: string
}

function MetricCard({
  title,
  value,
  loading,
  error,
  unavailableMessage,
}: MetricCardProps) {
  return (
    <Card style={{ flex: '1 1 220px', minWidth: 220 }}>
      <Flex direction="column" gap="2">
        <Text size="2" color="gray">
          {title}
        </Text>

        {unavailableMessage ? (
          <Text size="2" color="gray">
            {unavailableMessage}
          </Text>
        ) : loading ? (
          <Text size="3">Loading...</Text>
        ) : error ? (
          <Text size="2" color="red">
            {error}
          </Text>
        ) : (
          <Text size="7" weight="bold">
            {value ?? '0'}
          </Text>
        )}
      </Flex>
    </Card>
  )
}
