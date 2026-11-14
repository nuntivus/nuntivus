import { Badge, Card, Flex, Heading, Link, Select, Table, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { ErrorPreview } from '../components/errors/error-preview'
import { EntityReference } from '../components/links/entity-reference'
import { DetailIndicator, NavigableTableRow } from '../components/tables/table-actions'
import { DashboardTimeChart } from '../components/observability/dashboard-time-chart'
import {
  useDashboardOverviewQuery,
  useDashboardTimeSeriesQuery,
  useRecentEventsQuery,
  useRecentFailuresQuery,
  useSuccessfulDeliveriesCountQuery,
} from '../features/dashboard/use-dashboard-data'
import { getApiErrorMessage } from '../services/api/api-client'

export function DashboardPage() {
  const [refreshInterval, setRefreshInterval] = useState(30_000)
  const [rangeSeconds, setRangeSeconds] = useState(3600)
  const overviewQuery = useDashboardOverviewQuery(refreshInterval)
  const successfulQuery = useSuccessfulDeliveriesCountQuery(refreshInterval)
  const recentEventsQuery = useRecentEventsQuery(refreshInterval)
  const recentFailuresQuery = useRecentFailuresQuery(refreshInterval)
  const timeSeriesQuery = useDashboardTimeSeriesQuery(rangeSeconds, refreshInterval)

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
    <Flex direction="column" gap="4" className="dashboard-overview">
      <div className="dashboard-overview-header">
        <div>
          <Heading size="6">Overview</Heading>
          <Text size="2" color="gray">
            Monitor delivery health, throughput, and recent platform activity.
          </Text>
        </div>
        <div className="dashboard-monitor-controls" aria-label="Dashboard monitoring controls">
          <span className="dashboard-live-status">
            <span aria-hidden="true" /> Live
          </span>
          <label className="dashboard-control-field" htmlFor="dashboard-range">
            <span>Window</span>
            <Select.Root value={String(rangeSeconds)} onValueChange={(value) => setRangeSeconds(Number(value))}>
              <Select.Trigger id="dashboard-range" aria-label="Graph window" />
              <Select.Content>
                <Select.Item value="15">15 seconds</Select.Item>
                <Select.Item value="30">30 seconds</Select.Item>
                <Select.Item value="60">1 minute</Select.Item>
                <Select.Item value="3600">1 hour</Select.Item>
              </Select.Content>
            </Select.Root>
          </label>
          <label className="dashboard-control-field" htmlFor="dashboard-refresh">
            <span>Refresh</span>
            <Select.Root value={String(refreshInterval)} onValueChange={(value) => setRefreshInterval(Number(value))}>
              <Select.Trigger id="dashboard-refresh" aria-label="Refresh interval" />
              <Select.Content>
                <Select.Item value="15000">15 seconds</Select.Item>
                <Select.Item value="30000">30 seconds</Select.Item>
                <Select.Item value="60000">1 minute</Select.Item>
                <Select.Item value="3600000">1 hour</Select.Item>
              </Select.Content>
            </Select.Root>
          </label>
        </div>
      </div>

      <div className="dashboard-metric-grid">
        <MetricCard
          title="Successful deliveries"
          value={successfulQuery.data?.toString()}
          tone="success"
          loading={successfulQuery.isLoading}
          error={successfulQuery.isError
            ? getApiErrorMessage(successfulQuery.error, 'Unable to load successful deliveries.')
            : undefined}
        />

        <MetricCard
          title="Failed deliveries"
          value={overview?.totalFailedDeliveries.toString()}
          tone="warning"
          loading={overviewQuery.isLoading}
          error={overviewQuery.isError
            ? getApiErrorMessage(overviewQuery.error, 'Unable to load failed deliveries.')
            : undefined}
        />

        <MetricCard
          title="Dead deliveries"
          value={overview?.totalDeadDeliveries.toString()}
          tone="danger"
          loading={overviewQuery.isLoading}
          error={overviewQuery.isError
            ? getApiErrorMessage(overviewQuery.error, 'Unable to load dead deliveries.')
            : undefined}
        />

        <MetricCard
          title="Delivery success rate"
          value={formatRatePercentage(successfulDeliveries, trackedDeliveriesTotal)}
          tone="success"
          loading={ratesLoading}
          error={ratesError}
        />

        <MetricCard
          title="Delivery failure rate"
          value={formatRatePercentage(failedOrDeadDeliveries, trackedDeliveriesTotal)}
          tone="danger"
          loading={ratesLoading}
          error={ratesError}
        />
      </div>

      <div className="responsive-panel-grid">
        <DashboardTimeChart
          title="Delivery outcomes"
          buckets={timeSeriesQuery.data?.buckets ?? []}
          series={[
            { key: 'delivered', label: 'Delivered', color: 'var(--green-9)' },
            { key: 'failed', label: 'Failed', color: 'var(--orange-9)' },
            { key: 'dead', label: 'Dead', color: 'var(--red-9)' },
          ]}
          loading={timeSeriesQuery.isLoading}
          error={timeSeriesQuery.isError
            ? getApiErrorMessage(timeSeriesQuery.error, 'Unable to load delivery history.')
            : undefined}
        />
        <DashboardTimeChart
          title="Event and delivery throughput"
          buckets={timeSeriesQuery.data?.buckets ?? []}
          series={[
            { key: 'events', label: 'Events received', color: 'var(--accent-9)' },
            { key: 'deliveries', label: 'Deliveries updated', color: 'var(--purple-9)' },
          ]}
          loading={timeSeriesQuery.isLoading}
          error={timeSeriesQuery.isError
            ? getApiErrorMessage(timeSeriesQuery.error, 'Unable to load throughput history.')
            : undefined}
        />
      </div>

      <div className="responsive-panel-grid">
        <Card className="dashboard-activity-panel">
          <Flex direction="column" gap="3">
            <Flex justify="between" align="center" gap="3">
              <div>
                <Heading size="4">Recent events</Heading>
                <Text size="1" color="gray">Latest events accepted by the platform</Text>
              </div>
              <Link asChild size="1">
                <NavLink to="/events">View all</NavLink>
              </Link>
            </Flex>

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
              <div className="responsive-table dashboard-activity-table">
                <Table.Root size="1" aria-label="Recent events">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>Event</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Source</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Received</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell className="table-action-column">Action</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {recentEvents.map((item) => (
                      <NavigableTableRow key={item.id} to={`/events/${encodeURIComponent(item.id)}`} label={`event ${item.type}`}>
                        <Table.RowHeaderCell><Text weight="medium" size="2">{item.type}</Text></Table.RowHeaderCell>
                        <Table.Cell><EntityReference kind="source" id={item.source} /></Table.Cell>
                        <Table.Cell className="table-nowrap">{formatTimestamp(item.receivedAt)}</Table.Cell>
                        <Table.Cell className="table-action-column">
                          <div className="table-row-actions">
                            <DetailIndicator />
                          </div>
                        </Table.Cell>
                      </NavigableTableRow>
                    ))}
                  </Table.Body>
                </Table.Root>
              </div>
            )}
          </Flex>
        </Card>

        <Card className="dashboard-activity-panel">
          <Flex direction="column" gap="3">
            <Flex justify="between" align="center" gap="3">
              <div>
                <Heading size="4">Recent failures</Heading>
                <Text size="1" color="gray">Deliveries that need operator attention</Text>
              </div>
              <Link asChild size="1">
                <NavLink to="/deliveries/failures">View all</NavLink>
              </Link>
            </Flex>

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
              <div className="responsive-table dashboard-activity-table">
                <Table.Root size="1" aria-label="Recent delivery failures">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>Event</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Customer</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Error</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell className="table-action-column">Action</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {recentFailures.map((item) => (
                      <NavigableTableRow key={item.deliveryId} to={`/deliveries/${encodeURIComponent(item.deliveryId)}`} label={`delivery ${item.deliveryId}`}>
                        <Table.RowHeaderCell><Badge color="red" variant="soft">{item.eventType}</Badge></Table.RowHeaderCell>
                        <Table.Cell><EntityReference kind="customer" id={item.customerId} /></Table.Cell>
                        <Table.Cell>{item.attemptCount}</Table.Cell>
                        <Table.Cell><ErrorPreview error={item.lastError} /></Table.Cell>
                        <Table.Cell className="table-action-column">
                          <div className="table-row-actions">
                            <DetailIndicator />
                          </div>
                        </Table.Cell>
                      </NavigableTableRow>
                    ))}
                  </Table.Body>
                </Table.Root>
              </div>
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

function formatTimestamp(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

interface MetricCardProps {
  title: string
  value?: string
  loading?: boolean
  error?: string
  unavailableMessage?: string
  tone?: 'neutral' | 'success' | 'warning' | 'danger'
}

function MetricCard({
  title,
  value,
  loading,
  error,
  unavailableMessage,
  tone = 'neutral',
}: MetricCardProps) {
  return (
    <Card className={`dashboard-metric dashboard-metric-${tone}`}>
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
