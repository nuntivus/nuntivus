import { Badge, Button, Card, Flex, Heading, Spinner, Table, Text } from '@radix-ui/themes'
import { NavLink } from 'react-router-dom'
import type {
  DestinationHealthSnapshot,
  DestinationHealthStatus,
} from '../../services/api/destination-health-api'
import { getApiErrorMessage } from '../../services/api/api-client'

function normalizeStatus(status: DestinationHealthStatus) {
  const value = String(status).toLowerCase()
  if (value === '0' || value === 'healthy') {
    return 'Healthy'
  }

  if (value === '1' || value === 'degraded') {
    return 'Degraded'
  }

  if (value === '2' || value === 'unhealthy') {
    return 'Failing'
  }

  return String(status)
}

function statusColor(status: DestinationHealthStatus): 'green' | 'amber' | 'red' | 'gray' {
  const normalized = normalizeStatus(status).toLowerCase()
  if (normalized === 'healthy') {
    return 'green'
  }

  if (normalized === 'degraded') {
    return 'amber'
  }

  if (normalized === 'failing') {
    return 'red'
  }

  return 'gray'
}

function formatLatency(value: number | null) {
  return value === null ? '-' : `${value.toFixed(1)} ms`
}

function formatTimestamp(value: string | null) {
  return value ? new Date(value).toLocaleString() : '-'
}

type DestinationHealthSummaryProps = {
  title?: string
  customerId?: string
  contractId?: string
  items: DestinationHealthSnapshot[]
  isLoading: boolean
  isError: boolean
  error: unknown
  canReadFailures: boolean
}

export function DestinationHealthSummary({
  title = 'Destination health',
  customerId,
  contractId,
  items,
  isLoading,
  isError,
  error,
  canReadFailures,
}: DestinationHealthSummaryProps) {
  return (
    <Card>
      <Flex direction="column" gap="3">
        <Heading size="4">{title}</Heading>
        <Text size="2" color="gray">
          Health is based on the latest backend snapshot and may not reflect real-time destination status.
        </Text>

        {isLoading ? (
          <Flex align="center" gap="2">
            <Spinner size="1" />
            <Text size="2" color="gray">Loading destination health...</Text>
          </Flex>
        ) : isError ? (
          <Text size="2" color="red">
            {getApiErrorMessage(error, 'Unable to load destination health.')}
          </Text>
        ) : items.length === 0 ? (
          <Text size="2" color="gray">
            No destination health snapshot is available yet.
          </Text>
        ) : (
          <>
            <div className="responsive-table">
              <Table.Root aria-label="Destination health table">
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Destination</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Consecutive failures</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Last success</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Last failure</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Recent latency</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Snapshot updated</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {items.map((item) => (
                  <Table.Row key={item.contractId}>
                    <Table.Cell>
                      <Badge color={statusColor(item.status)} variant="soft">
                        {normalizeStatus(item.status)}
                      </Badge>
                    </Table.Cell>
                    <Table.Cell>{item.destinationType}</Table.Cell>
                    <Table.Cell>{item.consecutiveFailures}</Table.Cell>
                    <Table.Cell>{formatTimestamp(item.lastSuccessAt)}</Table.Cell>
                    <Table.Cell>{formatTimestamp(item.lastFailureAt)}</Table.Cell>
                    <Table.Cell>{formatLatency(item.lastLatencyMs)}</Table.Cell>
                    <Table.Cell>{new Date(item.updatedAt).toLocaleString()}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
              </Table.Root>
            </div>

            {canReadFailures ? (
              <Flex gap="2" wrap="wrap">
                {customerId ? (
                  <Button asChild variant="soft" size="1">
                    <NavLink to={`/deliveries/failures?customerId=${encodeURIComponent(customerId)}`}>
                      View failed deliveries
                    </NavLink>
                  </Button>
                ) : null}

                {contractId ? (
                  <Button asChild variant="soft" size="1">
                    <NavLink
                      to={`/deliveries/failures?contractId=${encodeURIComponent(contractId)}`}
                    >
                      View failed deliveries for contract
                    </NavLink>
                  </Button>
                ) : null}
              </Flex>
            ) : (
              <Text size="2" color="gray">
                Failed deliveries links are not available for your role.
              </Text>
            )}
          </>
        )}
      </Flex>
    </Card>
  )
}
