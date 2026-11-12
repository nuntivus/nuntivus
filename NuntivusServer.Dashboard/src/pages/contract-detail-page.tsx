import {
  Badge,
  Button,
  Card,
  Flex,
  Heading,
  IconButton,
  Table,
  Text,
} from '@radix-ui/themes'
import { useState, type ComponentType, type ReactNode, type SVGProps } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { ErrorPreview } from '../components/errors/error-preview'
import { EntityReference } from '../components/links/entity-reference'
import { DestinationHealthSummary } from '../components/observability/destination-health-summary'
import {
  CalendarIcon,
  CheckCircleIcon,
  ClockIcon,
  CodeIcon,
  ContractsIcon,
  CopyIcon,
  CustomersIcon,
  DeliveriesIcon,
  EventsIcon,
  SettingsIcon,
} from '../components/icons/nav-icons'
import { DetailLoadingState } from '../components/states/detail-loading-state'
import {
  useContractDeliveriesQuery,
  useContractDetailQuery,
  useContractRecentFailuresQuery,
  useDisableContractMutation,
  useEnableContractMutation,
} from '../features/contracts/use-contracts'
import { useContractDestinationHealthQuery } from '../features/destination-health/use-destination-health'
import { useSourcesQuery } from '../features/sources/use-sources'
import { formatDeliveryStatus } from '../features/deliveries/delivery-status'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useNotifications } from '../hooks/use-notifications'
import { ApiError, getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'
import { ContractSloPanel } from '../features/contracts/contract-slo-panel'

function authenticationDetails(configurationText: string) {
  try {
    const parsed = JSON.parse(configurationText) as {
      authentication?: { type?: string; credential?: { kind?: string; version?: number } }
    }
    const type = parsed.authentication?.type?.trim() || 'none'
    const credential = parsed.authentication?.credential
    return {
      type,
      details: credential
        ? `${credential.kind === 'ref' ? 'Referenced' : 'Protected'} credential${credential.version ? ` v${credential.version}` : ''}`
        : 'No authentication configured',
    }
  } catch {
    return { type: 'Unavailable', details: 'Configuration could not be parsed' }
  }
}

type ContractIcon = ComponentType<SVGProps<SVGSVGElement>>

function ContractMetric({ icon: Icon, label, value, supporting, tone = 'accent', action }: {
  icon: ContractIcon
  label: string
  value: ReactNode
  supporting: string
  tone?: 'accent' | 'danger' | 'success' | 'violet'
  action?: ReactNode
}) {
  return (
    <Card className={`contract-metric contract-tone-${tone}`}>
      <span className="contract-metric-icon"><Icon /></span>
      <div className="contract-metric-content">
        <Text size="1" color="gray">{label}</Text>
        <div className="contract-metric-value">{value}</div>
        <Text size="1" color="gray">{supporting}</Text>
      </div>
      {action}
    </Card>
  )
}

function ContractSectionTitle({ icon: Icon, children }: { icon: ContractIcon; children: ReactNode }) {
  return (
    <Flex align="center" gap="2" className="contract-section-title">
      <Icon />
      <Heading size="4">{children}</Heading>
    </Flex>
  )
}

function ContractInfoRow({ icon: Icon, label, children, action }: {
  icon: ContractIcon
  label: string
  children: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="contract-info-row">
      <Icon />
      <Text size="2" color="gray" weight="medium">{label}</Text>
      <div className="contract-info-value">{children}</div>
      {action}
    </div>
  )
}

export function ContractDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadContracts = hasPermission(currentUser, 'contracts.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')
  const canReadDeliveries = hasPermission(currentUser, 'deliveries.read')
  const canReadFailures = hasPermission(currentUser, 'delivery-failures.read')

  const detailQuery = useContractDetailQuery(id)
  const sourcesQuery = useSourcesQuery(canReadContracts)
  const destinationHealthQuery = useContractDestinationHealthQuery(id)
  const deliveriesQuery = useContractDeliveriesQuery(canReadDeliveries ? id : undefined)
  const failuresQuery = useContractRecentFailuresQuery(canReadFailures ? id : undefined)

  const enableMutation = useEnableContractMutation()
  const disableMutation = useDisableContractMutation()
  const [copiedField, setCopiedField] = useState<string | null>(null)

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
    return <DetailLoadingState message="Loading contract detail..." />
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
  const authentication = authenticationDetails(contract.destinationConfiguration)
  const deliveries = deliveriesQuery.data?.items ?? []

  async function copyValue(value: string, label: string) {
    await navigator.clipboard.writeText(value)
    setCopiedField(label)
    window.setTimeout(() => setCopiedField((current) => current === label ? null : current), 1500)
  }

  async function enableContract() {
    try {
      await enableMutation.mutateAsync(contract.id)
      notifications.success('Contract enabled.', 'Contract detail')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to enable contract.'), 'Contract detail')
    }
  }

  async function disableContract() {
    const confirmed = await confirm({
      title: 'Disable contract',
      description: 'Disable this contract? Event matching can stop for this contract until it is enabled again.',
      confirmText: 'Disable contract',
      cancelText: 'Cancel',
      confirmColor: 'red',
    })

    if (!confirmed) return

    try {
      await disableMutation.mutateAsync(contract.id)
      notifications.success('Contract disabled.', 'Contract detail')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to disable contract.'), 'Contract detail')
    }
  }

  return (
    <Flex direction="column" gap="4" className="contract-detail-page">
      <Flex align="center" justify="between" wrap="wrap" gap="3" className="contract-detail-header">
        <Flex align="center" gap="3">
          <span className="contract-header-icon"><ContractsIcon /></span>
          <div>
            <Flex align="center" gap="2" wrap="wrap">
              <Heading size="6">Contract: {contract.name ?? contract.id}</Heading>
              <Badge color={contract.enabled ? 'green' : 'gray'} variant="soft">
                {contract.enabled ? 'Enabled' : 'Disabled'}
              </Badge>
            </Flex>
            <Text size="2" color="gray">Contract configuration and settings for event delivery.</Text>
          </div>
        </Flex>
        <Flex gap="2" wrap="wrap">
          <Button asChild variant="soft">
            <NavLink to="/contracts">Back to list</NavLink>
          </Button>
          <Button asChild variant="soft">
            <NavLink to={`/test-events?contractId=${encodeURIComponent(contract.id)}`}>
              Test event
            </NavLink>
          </Button>
          <Button
            variant="solid"
            onClick={() => navigate(`/contracts/${encodeURIComponent(contract.id)}/edit`)}
            disabled={!canWriteContracts}
          >
            Edit
          </Button>
        </Flex>
      </Flex>

      <div className="contract-metric-grid">
        <ContractMetric
          icon={CustomersIcon}
          label="Customer"
          value={<EntityReference kind="customer" id={contract.customerId} />}
          supporting="Contract owner"
          action={<IconButton size="1" variant="ghost" aria-label="Open customer" onClick={() => navigate(`/customers/${encodeURIComponent(contract.customerId)}`)}><CustomersIcon /></IconButton>}
        />
        <ContractMetric icon={EventsIcon} label="Event types" value={contract.eventTypes.join(', ') || '-'} supporting={`${contract.eventTypes.length} event type${contract.eventTypes.length === 1 ? '' : 's'} configured`} tone="danger" />
        <ContractMetric icon={DeliveriesIcon} label="Destinations" value={String(contract.destinations?.length ?? 1)} supporting="Delivery routes" tone="success" />
        <ContractMetric icon={SettingsIcon} label="Authentication" value={authentication.type === 'none' ? 'None' : authentication.type} supporting={authentication.details} tone="violet" />
      </div>

      <div className="contract-primary-grid">
        <Card className="contract-details-card">
          <ContractSectionTitle icon={CodeIcon}>Contract details</ContractSectionTitle>
          <div className="contract-info-list">
            <ContractInfoRow icon={ContractsIcon} label="Contract ID" action={<IconButton size="1" variant="ghost" color="gray" aria-label="Copy contract ID" onClick={() => void copyValue(contract.id, 'Contract ID')}><CopyIcon /></IconButton>}>{contract.id}</ContractInfoRow>
            <ContractInfoRow icon={CustomersIcon} label="Customer" action={<IconButton size="1" variant="ghost" color="gray" aria-label="Copy customer ID" onClick={() => void copyValue(contract.customerId, 'Customer ID')}><CopyIcon /></IconButton>}><EntityReference kind="customer" id={contract.customerId} /></ContractInfoRow>
            <ContractInfoRow icon={EventsIcon} label="Sources"><Flex gap="1" wrap="wrap">{contract.sourceIds.map((sourceId) => <EntityReference key={sourceId} kind="source" id={sourceId} label={sourcesQuery.data?.find((source) => source.id === sourceId)?.name} />)}</Flex></ContractInfoRow>
            <ContractInfoRow icon={EventsIcon} label="Event types"><Flex gap="1" wrap="wrap">{contract.eventTypes.map((eventType) => <Badge key={eventType} variant="soft">{eventType}</Badge>)}</Flex></ContractInfoRow>
            <ContractInfoRow icon={DeliveriesIcon} label="Destinations"><Flex gap="2" wrap="wrap">
              {contract.destinations?.map(binding => <Badge key={binding.id} variant="soft" color={binding.deliveryEnabled ? 'green' : 'gray'}>
                {binding.destinationId ? <EntityReference kind="destination" id={binding.destinationId} /> : `${binding.type} (detached)`}
              </Badge>)}
            </Flex></ContractInfoRow>
            <ContractInfoRow icon={CheckCircleIcon} label="Enabled state"><div><Badge color={contract.enabled ? 'green' : 'gray'} variant="soft">{contract.enabled ? 'Enabled' : 'Disabled'}</Badge><Text as="p" size="1" color="gray">This contract is currently {contract.enabled ? 'active and can receive events' : 'inactive'}.</Text></div></ContractInfoRow>
            <ContractInfoRow icon={CalendarIcon} label="Created at"><div>{new Date(contract.createdAt).toLocaleString()}<Text as="p" size="1" color="gray">Last updated {new Date(contract.updatedAt).toLocaleString()}</Text></div></ContractInfoRow>
          </div>
        </Card>

        <div className="contract-side-stack">
          <Card className="contract-action-card">
            <ContractSectionTitle icon={SettingsIcon}>Contract actions</ContractSectionTitle>
            <div className={`contract-state-callout ${contract.enabled ? 'enabled' : 'disabled'}`}>
              <CheckCircleIcon />
              <div><Text size="2" weight="bold">Contract is {contract.enabled ? 'enabled' : 'disabled'}</Text><Text size="1">This contract {contract.enabled ? 'can receive and process events' : 'will not receive new events'}.</Text></div>
            </div>
            {enableMutation.isError || disableMutation.isError ? <Text size="2" color="red">{getApiErrorMessage(enableMutation.error || disableMutation.error, 'Unable to update contract state.')}</Text> : null}
            <Flex gap="2" wrap="wrap">
              <Button type="button" variant="soft" disabled={!canWriteContracts || pending || contract.enabled} onClick={() => void enableContract()}>Enable</Button>
              <Button type="button" color="red" variant="soft" disabled={!canWriteContracts || pending || !contract.enabled} onClick={() => void disableContract()}>Disable</Button>
            </Flex>
          </Card>

          <Card className="contract-auth-card">
            <ContractSectionTitle icon={SettingsIcon}>Authentication summary</ContractSectionTitle>
            <ContractInfoRow icon={SettingsIcon} label="Authentication type"><Badge color="gray" variant="soft">{authentication.type}</Badge></ContractInfoRow>
            <ContractInfoRow icon={CodeIcon} label="Details">{authentication.details}</ContractInfoRow>
          </Card>
        </div>
      </div>

      <div className="contract-secondary-grid">
        <Card className="contract-settings-card">
          <ContractSectionTitle icon={SettingsIcon}>Advanced contract settings</ContractSectionTitle>
          {([
            ['Schema targets', contract.schemaTargets],
            ['Filters', contract.filter],
            ['Payload transformation', contract.transformation],
            ['Rate limit', contract.rateLimit],
            ['Ordering', contract.ordering],
          ] as const).map(([label, value], index) => (
            <div className="contract-setting-row" key={label}>
              <span className={`contract-setting-icon contract-setting-${index}`}><SettingsIcon /></span>
              <Text size="2" weight="medium">{label}</Text>
              <div><Text size="2">{value == null || (Array.isArray(value) && !value.length) ? 'Not configured' : 'Configured'}</Text><Text as="p" size="1" color="gray">{value == null || (Array.isArray(value) && !value.length) ? `No ${label.toLowerCase()} defined for this contract.` : JSON.stringify(value)}</Text></div>
              <Button size="1" variant="soft" onClick={() => navigate(`/contracts/${encodeURIComponent(contract.id)}/edit`)} disabled={!canWriteContracts}>Configure</Button>
            </div>
          ))}
        </Card>

        <Card className="contract-activity-card">
          <Flex align="center" justify="between" className="contract-section-title">
            <Flex align="center" gap="2"><ClockIcon /><Heading size="4">Recent activity</Heading></Flex>
            <Button size="1" variant="ghost" onClick={() => navigate(`/deliveries?contractId=${encodeURIComponent(contract.id)}`)} disabled={!canReadDeliveries}>View all</Button>
          </Flex>
          {!canReadDeliveries ? <Text size="2" color="gray">Activity is not available for your role.</Text> : deliveriesQuery.isLoading ? <Text size="2" color="gray">Loading activity...</Text> : deliveriesQuery.isError ? <Text size="2" color="red">{getApiErrorMessage(deliveriesQuery.error, 'Unable to load activity.')}</Text> : deliveries.length === 0 ? (
            <div className="contract-empty-activity"><span><ContractsIcon /></span><Text size="2" weight="bold">No recent events</Text><Text size="1" color="gray">There are no recent events for this contract yet.</Text><Button asChild size="1" variant="soft"><NavLink to={`/test-events?contractId=${encodeURIComponent(contract.id)}`}>Send test event</NavLink></Button></div>
          ) : (
            <div className="contract-activity-list">{deliveries.slice(0, 4).map((delivery) => <NavLink key={delivery.id} to={`/deliveries/${encodeURIComponent(delivery.id)}`} className="contract-activity-row"><span className={formatDeliveryStatus(delivery.status).toLowerCase()}><DeliveriesIcon /></span><div><Text size="2" weight="medium">{delivery.eventType}</Text><Text as="p" size="1" color="gray">{delivery.id}</Text></div><Badge color={formatDeliveryStatus(delivery.status).toLowerCase() === 'delivered' ? 'green' : 'red'} variant="soft">{formatDeliveryStatus(delivery.status)}</Badge></NavLink>)}</div>
          )}
        </Card>
      </div>

      {copiedField ? <Text size="1" color="gray" className="delivery-copy-feedback">{copiedField} copied</Text> : null}

      <div className="contract-wide-panel"><DestinationHealthSummary title="Destination health" contractId={contract.id} items={destinationHealthQuery.data ? [destinationHealthQuery.data] : []} isLoading={destinationHealthQuery.isLoading} isError={destinationHealthQuery.isError} error={destinationHealthQuery.error} canReadFailures={canReadFailures} /></div>

      <ContractSloPanel contractId={contract.id} canWrite={canWriteContracts} />

      <Card className="contract-wide-panel">
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

      <Card className="contract-wide-panel">
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
                    <Table.Cell><ErrorPreview error={failure.lastError} /></Table.Cell>
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
