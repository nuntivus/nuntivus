import {
  Button,
  Card,
  Flex,
  Heading,
  Select,
  Text,
  TextArea,
  TextField,
} from '@radix-ui/themes'
import { useMemo, useState, type FormEvent } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { DestinationConfigurationForm } from '../features/contracts/destination-forms/destination-configuration-form'
import {
  createDefaultDestinationFormState,
  mapDestinationFormToConfig,
  validateDestinationFormState,
} from '../features/contracts/destination-forms/types'
import { useCreateContractMutation } from '../features/contracts/use-contracts'
import { useCustomersQuery } from '../features/customers/use-customers'
import { getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

function parseEventTypes(value: string) {
  return value
    .split(/[\n,]+/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)
}

export function ContractCreatePage() {
  const navigate = useNavigate()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadCustomers = hasPermission(currentUser, 'customers.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')

  const createMutation = useCreateContractMutation()

  const customersQuery = useCustomersQuery({
    page: 1,
    pageSize: 100,
    enabled: true,
    sortBy: 'name',
    sortDirection: 'asc',
  })

  const [customerId, setCustomerId] = useState('')
  const [name, setName] = useState('')
  const [eventTypesInput, setEventTypesInput] = useState('')
  const [destination, setDestination] = useState(createDefaultDestinationFormState())

  const eventTypes = useMemo(() => parseEventTypes(eventTypesInput), [eventTypesInput])

  const validationError = useMemo(() => {
    if (!customerId.trim()) {
      return 'Customer is required.'
    }

    if (eventTypes.length === 0) {
      return 'At least one event type is required.'
    }

    return validateDestinationFormState(destination)
  }, [customerId, destination, eventTypes.length])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteContracts || createMutation.isPending || validationError) {
      return
    }

    const result = await createMutation.mutateAsync({
      customerId: customerId.trim(),
      name: name.trim() || null,
      eventTypes,
      destination: mapDestinationFormToConfig(destination),
      url: '',
      authenticationType: 'none',
    })

    navigate(`/contracts/${encodeURIComponent(result.id)}`)
  }

  if (!canWriteContracts) {
    return (
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Heading size="5">Create contract</Heading>
          <Text color="gray">You do not have permission to create contracts.</Text>
          <Button asChild>
            <NavLink to="/forbidden">Go to 403 page</NavLink>
          </Button>
        </Flex>
      </Card>
    )
  }

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Create contract</Heading>

      <Card>
        <form onSubmit={handleSubmit}>
          <Flex direction="column" gap="3">
            <Text size="2" color="gray">
              Customer
            </Text>
            <Select.Root value={customerId} onValueChange={setCustomerId}>
              <Select.Trigger placeholder="Select customer" />
              <Select.Content>
                {!canReadCustomers || customersQuery.isLoading ? null : (customersQuery.data?.items ?? []).map((customer) => (
                  <Select.Item key={customer.id} value={customer.id}>
                    {customer.name} ({customer.id})
                  </Select.Item>
                ))}
              </Select.Content>
            </Select.Root>

            <Text size="2" color="gray">
              Contract name
            </Text>
            <TextField.Root
              placeholder="Optional friendly name"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />

            <Text size="2" color="gray">
              Event types
            </Text>
            <TextArea
              value={eventTypesInput}
              onChange={(event) => setEventTypesInput(event.target.value)}
              placeholder="orders.created, orders.updated"
              rows={4}
            />

            <DestinationConfigurationForm
              value={destination}
              onChange={setDestination}
            />

            {validationError ? (
              <Text size="2" color="red">
                {validationError}
              </Text>
            ) : null}

            {createMutation.isError ? (
              <Text size="2" color="red">
                {getApiErrorMessage(createMutation.error, 'Unable to create contract.')}
              </Text>
            ) : null}

            <Flex gap="2">
              <Button
                type="submit"
                disabled={createMutation.isPending || !!validationError}
              >
                {createMutation.isPending ? 'Submitting...' : 'Create contract'}
              </Button>
              <Button asChild variant="soft" type="button">
                <NavLink to="/contracts">Cancel</NavLink>
              </Button>
            </Flex>
          </Flex>
        </form>
      </Card>
    </Flex>
  )
}
