import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  Link,
  Table,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { DestinationHealthSummary } from '../components/observability/destination-health-summary'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { useCustomerDestinationHealthQuery } from '../features/destination-health/use-destination-health'
import {
  useCustomerContractsQuery,
  useCustomerDeliveriesQuery,
  useCustomerDetailQuery,
  useCustomerRecentFailuresQuery,
  useDisableCustomerMutation,
  useEnableCustomerMutation,
  useUpdateCustomerMutation,
} from '../features/customers/use-customers'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
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

export function CustomerDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadCustomers = hasPermission(currentUser, 'customers.read')
  const canWriteCustomers = hasPermission(currentUser, 'customers.write')
  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const canReadFailures = hasPermission(currentUser, 'delivery-failures.read')

  const detailQuery = useCustomerDetailQuery(id)
  const destinationHealthQuery = useCustomerDestinationHealthQuery(id)
  const contractsQuery = useCustomerContractsQuery(canReadContracts ? id : undefined)
  const deliveriesQuery = useCustomerDeliveriesQuery(canReadDeliveries ? id : undefined)
  const failuresQuery = useCustomerRecentFailuresQuery(canReadFailures ? id : undefined)

  const updateMutation = useUpdateCustomerMutation(id ?? '')
  const enableMutation = useEnableCustomerMutation()
  const disableMutation = useDisableCustomerMutation()

  const customer = detailQuery.data

  const detailError = useMemo(
    () => getApiErrorMessage(detailQuery.error, 'Unable to load customer details.'),
    [detailQuery.error],
  )

  if (!canReadCustomers) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Customer detail</Heading>
          <Text color="gray">You do not have permission to read customers.</Text>
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
        <Text color="red">Invalid customer id.</Text>
      </Card>
    )
  }

  if (detailQuery.isLoading) {
    return <DetailLoadingState message="Loading customer detail..." />
  }

  if (detailQuery.isError || !customer) {
    const forbidden = detailQuery.error instanceof ApiError && detailQuery.error.status === 403
    const notFound = detailQuery.error instanceof ApiError && detailQuery.error.status === 404

    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Customer detail</Heading>
          <Text color={forbidden || notFound ? 'gray' : 'red'}>
            {forbidden
              ? 'The backend denied access (403).'
              : notFound
                ? 'Customer not found.'
                : detailError}
          </Text>
          <Button asChild variant="soft">
            <NavLink to="/customers">Back to customers</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  const togglePending =
    enableMutation.isPending || disableMutation.isPending || updateMutation.isPending

  return (
    <Flex direction="column" gap="4">
      <Flex align="center" justify="between" wrap="wrap" gap="3">
        <Heading size="6">Customer: {customer.id}</Heading>
        <Flex gap="2" wrap="wrap">
          <Button asChild variant="soft">
            <NavLink to="/customers">Back to list</NavLink>
          </Button>
          <Button
            variant="soft"
            onClick={() => navigate(`/deliveries?customerId=${encodeURIComponent(customer.id)}`)}
            disabled={!canReadDeliveries}
          >
            View deliveries
          </Button>
        </Flex>
      </Flex>

      <CustomerInfoEditor
        key={`${customer.id}:${customer.updatedAt}`}
        id={customer.id}
        name={customer.name}
        enabled={customer.enabled}
        canWriteCustomers={canWriteCustomers}
        mutationError={
          getApiErrorMessage(updateMutation.error, '') ||
          getApiErrorMessage(enableMutation.error, '') ||
          getApiErrorMessage(disableMutation.error, '')
        }
        pending={togglePending}
        onSave={async (name) => {
          try {
            await updateMutation.mutateAsync({ name })
            notifications.success('Customer saved.', 'Customer detail')
          } catch (error) {
            notifications.error(getApiErrorMessage(error, 'Unable to save customer.'), 'Customer detail')
          }
        }}
        onEnable={async () => {
          try {
            await enableMutation.mutateAsync(customer.id)
            notifications.success('Customer enabled.', 'Customer detail')
          } catch (error) {
            notifications.error(getApiErrorMessage(error, 'Unable to enable customer.'), 'Customer detail')
          }
        }}
        onDisable={async () => {
          const confirmed = await confirm({
            title: 'Disable customer',
            description:
              'Disable this customer? New deliveries for this customer may be blocked until it is enabled again.',
            confirmText: 'Disable customer',
            cancelText: 'Cancel',
            confirmColor: 'red',
          })

          if (!confirmed) {
            return
          }

          try {
            await disableMutation.mutateAsync(customer.id)
            notifications.success('Customer disabled.', 'Customer detail')
          } catch (error) {
            notifications.error(getApiErrorMessage(error, 'Unable to disable customer.'), 'Customer detail')
          }
        }}
      />

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Customer contracts</Heading>
          {!canReadContracts ? (
            <Text size="2" color="gray">
              Contracts are not available for your role.
            </Text>
          ) : contractsQuery.isLoading ? (
            <Text size="2" color="gray">Loading contracts...</Text>
          ) : contractsQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(contractsQuery.error, 'Unable to load contracts.')}
            </Text>
          ) : (contractsQuery.data?.items ?? []).length === 0 ? (
            <Text size="2" color="gray">No contracts found for this customer.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Enabled</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Event types</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Related</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {(contractsQuery.data?.items ?? []).map((contract) => (
                  <Table.Row key={contract.id}>
                    <Table.RowHeaderCell>{contract.id}</Table.RowHeaderCell>
                    <Table.Cell>{contract.name ?? '-'}</Table.Cell>
                    <Table.Cell>
                      <Badge color={contract.enabled ? 'green' : 'gray'}>
                        {contract.enabled ? 'Enabled' : 'Disabled'}
                      </Badge>
                    </Table.Cell>
                    <Table.Cell>{contract.eventTypes.join(', ') || '-'}</Table.Cell>
                    <Table.Cell>
                      <Button
                        size="1"
                        variant="ghost"
                        onClick={() =>
                          navigate(
                            `/deliveries?customerId=${encodeURIComponent(customer.id)}&contractId=${encodeURIComponent(contract.id)}`,
                          )
                        }
                        disabled={!canReadDeliveries}
                      >
                        Deliveries
                      </Button>
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <DestinationHealthSummary
        title="Destination health"
        customerId={customer.id}
        items={destinationHealthQuery.data ?? []}
        isLoading={destinationHealthQuery.isLoading}
        isError={destinationHealthQuery.isError}
        error={destinationHealthQuery.error}
        canReadFailures={canReadFailures}
      />

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Customer deliveries</Heading>
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
            <Text size="2" color="gray">No deliveries found for this customer.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>ID</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Event type</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Updated</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {(deliveriesQuery.data?.items ?? []).map((delivery) => (
                  <Table.Row key={delivery.id}>
                    <Table.RowHeaderCell>{delivery.id}</Table.RowHeaderCell>
                    <Table.Cell>{delivery.eventType}</Table.Cell>
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
            <Text size="2" color="gray">No recent failures for this customer.</Text>
          ) : (
            <Table.Root>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>Delivery</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Event type</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Error</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Attempts</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Next retry</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {(failuresQuery.data?.items ?? []).map((failure) => (
                  <Table.Row key={failure.deliveryId}>
                    <Table.RowHeaderCell>{failure.deliveryId}</Table.RowHeaderCell>
                    <Table.Cell>{failure.eventType}</Table.Cell>
                    <Table.Cell>{failure.lastError ?? '-'}</Table.Cell>
                    <Table.Cell>{failure.attemptCount}</Table.Cell>
                    <Table.Cell>
                      {failure.nextAttemptAt
                        ? new Date(failure.nextAttemptAt).toLocaleString()
                        : '-'}
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Flex>
      </Card>

      <Flex justify="between" wrap="wrap" gap="3">
        <Link asChild>
          <NavLink to="/customers">Back to customer list</NavLink>
        </Link>
        <Link asChild>
          <NavLink to="/users">Go to users & RBAC</NavLink>
        </Link>
      </Flex>
    </Flex>
  )
}

interface CustomerInfoEditorProps {
  id: string
  name: string
  enabled: boolean
  canWriteCustomers: boolean
  pending: boolean
  mutationError: string
  onSave: (name: string) => Promise<unknown>
  onEnable: () => Promise<unknown>
  onDisable: () => Promise<unknown>
}

function CustomerInfoEditor({
  id,
  name,
  enabled,
  canWriteCustomers,
  pending,
  mutationError,
  onSave,
  onEnable,
  onDisable,
}: CustomerInfoEditorProps) {
  const [draftName, setDraftName] = useState(name)
  const nameError = !draftName.trim() ? 'Name is required.' : null

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteCustomers || !!nameError) {
      return
    }

    await onSave(draftName.trim())
  }

  return (
    <Card>
      <form onSubmit={handleSubmit}>
        <Flex direction="column" gap="3">
          <Heading size="4">Customer information</Heading>

          <Text size="2" color="gray">
            Customer ID
          </Text>
          <Text weight="medium">{id}</Text>

          <Text as="label" htmlFor="customer-name" size="2" color="gray">
            Name
          </Text>
          <TextField.Root
            id="customer-name"
            value={draftName}
            onChange={(event) => setDraftName(event.target.value)}
            disabled={!canWriteCustomers || pending}
            aria-invalid={nameError ? 'true' : 'false'}
            aria-describedby={nameError ? 'customer-name-error' : undefined}
          />
          <FieldErrorText id="customer-name-error" message={nameError} />

          <Text size="2" color="gray">
            Enabled status
          </Text>
          <Badge color={enabled ? 'green' : 'gray'}>
            {enabled ? 'Enabled' : 'Disabled'}
          </Badge>

          <FormErrorText message={mutationError} id="customer-submit-error" />

          <Flex gap="2" wrap="wrap">
            <Button
              type="submit"
              disabled={!canWriteCustomers || pending || !!nameError}
            >
              {pending ? 'Saving...' : 'Save customer'}
            </Button>
            <Button
              type="button"
              variant="soft"
              disabled={!canWriteCustomers || pending || enabled}
              onClick={() => {
                void onEnable()
              }}
            >
              Enable
            </Button>
            <Button
              type="button"
              color="red"
              variant="soft"
              disabled={!canWriteCustomers || pending || !enabled}
              onClick={() => {
                void onDisable()
              }}
            >
              Disable
            </Button>
          </Flex>
        </Flex>
      </form>
    </Card>
  )
}
