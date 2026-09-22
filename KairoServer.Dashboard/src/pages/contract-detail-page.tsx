import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Link,
  Spinner,
  Table,
  Text,
} from '@radix-ui/themes'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import {
  useContractDeliveriesQuery,
  useContractDetailQuery,
  useContractRecentFailuresQuery,
  useDisableContractMutation,
  useEnableContractMutation,
} from '../features/contracts/use-contracts'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
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

function summarizeAuthentication(configurationText: string) {
  try {
    const parsed = JSON.parse(configurationText) as {
      authentication?: {
        type?: string
        credential?: { kind?: string; value?: string; version?: number }
      }
    }

    const authType = parsed.authentication?.type?.trim() || 'none'
    const credential = parsed.authentication?.credential

    if (!credential) {
      return `type=${authType}`
    }

    const kind = credential.kind?.trim() || 'unknown'
    const versionText = credential.version ? `, version=${credential.version}` : ''

    if (kind === 'ref') {
      return `type=${authType}, credential=reference${versionText}`
    }

    return `type=${authType}, credential=protected${versionText}`
  } catch {
    return 'unavailable'
  }
}

export function ContractDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')
  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const canReadFailures = hasPermission(currentUser, 'delivery-failures.read')

  const detailQuery = useContractDetailQuery(id)
  const deliveriesQuery = useContractDeliveriesQuery(canReadDeliveries ? id : undefined)
  const failuresQuery = useContractRecentFailuresQuery(canReadFailures ? id : undefined)

  const enableMutation = useEnableContractMutation()
  const disableMutation = useDisableContractMutation()

  if (!canReadContracts) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Contract detail</Heading>
          <Text color="gray">You do not have permission to read contracts.</Text>
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
        <Text color="red">Invalid contract id.</Text>
      </Card>
    )
  }

  if (detailQuery.isLoading) {
    return (
      <Flex align="center" gap="2">
        <Spinner size="1" />
        <Text>Loading contract detail...</Text>
      </Flex>
    )
  }

  if (detailQuery.isError || !detailQuery.data) {
    const forbidden = detailQuery.error instanceof ApiError && detailQuery.error.status === 403
    const notFound = detailQuery.error instanceof ApiError && detailQuery.error.status === 404

    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Contract detail</Heading>
          <Text color={forbidden || notFound ? 'gray' : 'red'}>
            {forbidden
              ? 'The backend denied access (403).'
              : notFound
                ? 'Contract not found.'
                : getApiErrorMessage(detailQuery.error, 'Unable to load contract.')}
          </Text>
          <Button asChild variant="soft">
            <NavLink to="/contracts">Back to contracts</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const contract = detailQuery.data.contract
  const pending = enableMutation.isPending || disableMutation.isPending
  const authSummary = summarizeAuthentication(contract.destinationConfiguration)

  return (
    <Flex direction="column" gap="4">
      <Flex align="center" justify="between" wrap="wrap" gap="3">
        <Heading size="6">Contract: {contract.name ?? contract.id}</Heading>
        <Flex gap="2">
          <Button asChild variant="soft">
            <NavLink to="/contracts">Back to list</NavLink>
          </Button>
          <Button
            variant="soft"
            onClick={() => navigate(`/contracts/${encodeURIComponent(contract.id)}/edit`)}
            disabled={!canWriteContracts}
          >
            Edit
          </Button>
        </Flex>
      </Flex>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Contract overview</Heading>

          <Text size="2" color="gray">
            Customer
          </Text>
          <Link asChild>
            <NavLink to={`/customers/${encodeURIComponent(contract.customerId)}`}>
              {contract.customerId}
            </NavLink>
          </Link>

          <Text size="2" color="gray">
            Event types
          </Text>
          <Text>{contract.eventTypes.join(', ') || '-'}</Text>

          <Text size="2" color="gray">
            Destination
          </Text>
          <Text>{contract.destinationType}</Text>

          <Text size="2" color="gray">
            Authentication summary
          </Text>
          <Text>{authSummary}</Text>

          <Text size="2" color="gray">
            Enabled state
          </Text>
          <Badge color={contract.enabled ? 'green' : 'gray'}>
            {contract.enabled ? 'Enabled' : 'Disabled'}
          </Badge>

          {enableMutation.isError || disableMutation.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(enableMutation.error || disableMutation.error, 'Unable to update contract state.')}
            </Text>
          ) : null}

          <Flex gap="2">
            <Button
              type="button"
              variant="soft"
              disabled={!canWriteContracts || pending || contract.enabled}
              onClick={() => {
                void enableMutation.mutateAsync(contract.id)
              }}
            >
              Enable
            </Button>
            <Button
              type="button"
              color="red"
              variant="soft"
              disabled={!canWriteContracts || pending || !contract.enabled}
              onClick={() => {
                void disableMutation.mutateAsync(contract.id)
              }}
            >
              Disable
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Recent deliveries</Heading>
          {!canReadDeliveries ? (
            <Text size="2" color="gray">
              Deliveries are not available for your role.
            </Text>
          ) : deliveriesQuery.isLoading ? (
            <Text size="2" color="gray">Loading deliveries...</Text>
          ) : deliveriesQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(deliveriesQuery.error, 'Unable to load deliveries.')}
            </Text>
          ) : (deliveriesQuery.data?.items ?? []).length === 0 ? (
            <Text size="2" color="gray">No deliveries for this contract.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Event type</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {(deliveriesQuery.data?.items ?? []).map((delivery) => (
                  <Table.Row key={delivery.id}>
                    <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                    <Table.Cell>{delivery.eventType}</Table.Cell>
                    <Table.Cell>{formatDeliveryStatus(delivery.status)}</Table.Cell>
                    <Table.Cell>{delivery.attemptCount}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Recent failures</Heading>
          {!canReadFailures ? (
            <Text size="2" color="gray">
              Recent failures are not available for your role.
            </Text>
          ) : failuresQuery.isLoading ? (
            <Text size="2" color="gray">Loading failures...</Text>
          ) : failuresQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(failuresQuery.error, 'Unable to load recent failures.')}
            </Text>
          ) : (failuresQuery.data?.items ?? []).length === 0 ? (
            <Text size="2" color="gray">No recent failures for this contract.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>Delivery</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Error</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {(failuresQuery.data?.items ?? []).map((failure) => (
                  <Table.Row key={failure.deliveryId}>
                    <Table.RowHeaderCell>{failure.deliveryId}</Table.RowHeaderCell>
                    <Table.Cell>{failure.lastError ?? '-'}</Table.Cell>
                    <Table.Cell>{failure.attemptCount}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Text size="2" color="gray">
        Test destination action will be added when backend support is exposed.
      </Text>
    </Flex>
  )
}
