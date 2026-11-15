import {
  Button,
  Card,
  Flex,
  Heading,
  Select,
  Text,
  TextField,
} from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { hasPermission } from '../auth/permissions'
import { FieldErrorText, FormErrorText } from '../components/forms/form-feedback'
import { getServerValidation } from '../components/forms/form-validation'
import { ConfigurationHeading } from '../components/forms/configuration-layout'
import { ContractsIcon } from '../components/icons/nav-icons'
import { ContractOptionsForm } from '../features/contracts/contract-options-form'
import { ContractSourceSelector } from '../features/contracts/contract-source-selector'
import { ContractDestinationSelector } from '../features/contracts/contract-destination-selector'
import { backendConfigToFormState, formStateToBackendConfig, validateAdvancedContractOptions } from '../features/contracts/contract-options'
import { useCreateContractMutation } from '../features/contracts/use-contracts'
import { useCustomersQuery } from '../features/customers/use-customers'
import { useEventDefinitionsQuery } from '../features/event-definitions/use-event-definitions'
import { useNotifications } from '../hooks/use-notifications'
import { useFormValidation } from '../hooks/use-form-validation'
import { getApiErrorMessage } from '../services/api/api-client'
import { useAppSelector } from '../store/hooks'

const createNewEventTypeValue = '__create_new__'

export function ContractCreatePage() {
  const navigate = useNavigate()
  const notifications = useNotifications()
  const { currentUser } = useAppSelector((state) => state.auth)

  const canReadCustomers = hasPermission(currentUser, 'customers.read')
  const canReadEventDefinitions = hasPermission(currentUser, 'event-definitions.read')
  const canWriteContracts = hasPermission(currentUser, 'contracts.write')

  const createMutation = useCreateContractMutation()

  const customersQuery = useCustomersQuery({
    page: 1,
    pageSize: 100,
    enabled: true,
    sortBy: 'name',
    sortDirection: 'asc',
  })
  const eventDefinitionsQuery = useEventDefinitionsQuery({ page: 1, pageSize: 100 })

  const [customerId, setCustomerId] = useState('')
  const [sourceIds, setSourceIds] = useState<string[]>([])
  const [name, setName] = useState('')
  const [selectedEventType, setSelectedEventType] = useState('')
  const [newEventType, setNewEventType] = useState('')
  const [destinationIds, setDestinationIds] = useState<string[]>([])
  const [optionsDraft, setOptionsDraft] = useState(() => backendConfigToFormState())
  const [rawEditing, setRawEditing] = useState<string[]>([])
  const [step, setStep] = useState<0 | 1 | 2>(0)
  const [basicsAttempted, setBasicsAttempted] = useState(false)
  const [destinationAttempted, setDestinationAttempted] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const eventType = selectedEventType === createNewEventTypeValue
    ? newEventType.trim()
    : selectedEventType.trim()
  const customerError = !customerId ? 'Customer is required.' : null
  const sourceError = sourceIds.length === 0 ? 'Select at least one Source.' : null
  const eventTypeError = !eventType ? 'Event type is required.' : null
  const destinationError = destinationIds.length === 0 ? 'Select at least one Destination.' : null
  const optionErrors = validateAdvancedContractOptions(optionsDraft, eventType ? [eventType] : [])
  const optionsValidation = useFormValidation(optionErrors)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canWriteContracts || createMutation.isPending) {
      return
    }

    if (step === 2) {
      const optionsValid = optionsValidation.validate()
      if (rawEditing.length || !optionsValid) {
        setSubmitError('Correct the advanced contract options before creating the contract.')
        return
      }
    }

    if (step === 0 || customerError || sourceError || eventTypeError) {
      setBasicsAttempted(true)
      if (customerError || sourceError || eventTypeError) {
        setStep(0)
        return
      }
      setStep(1)
      return
    }

    if (step === 1 || destinationError) {
      setDestinationAttempted(true)
      if (destinationError) {
        setStep(1)
        return
      }
      setStep(2)
      return
    }

    setSubmitError(null)

    try {
      const result = await createMutation.mutateAsync({
        customerId: customerId.trim(),
        sourceIds,
        name: name.trim() || null,
        eventTypes: [eventType],
        destinationIds,
        ...formStateToBackendConfig(optionsDraft),
        url: '',
        authenticationType: 'none',
      })

      notifications.success('Contract created successfully.', 'Create contract')
      navigate(`/contracts/${encodeURIComponent(result.id)}`)
    } catch (error) {
      setSubmitError(getServerValidation(error, 'Unable to create contract.').formError)
      notifications.error(getApiErrorMessage(error, 'Unable to create contract.'), 'Create contract')
    }
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

      <Card className="configuration-editor settings-editor-card">
        <form onSubmit={handleSubmit}>
          <Flex direction="column" gap="3">
            <ConfigurationHeading title="Contract configuration" description="Choose the customer, sources, event types and destinations for this contract." icon={<ContractsIcon />} />
            <Text size="2" color="gray" role="status">
              {['Basics', 'Destination', 'Review'][step]} · Step {step + 1} of 3
            </Text>

            {step === 0 ? (
              <>
                <Text id="contract-create-customer-label" size="2" color="gray">Customer</Text>
                <Select.Root value={customerId} onValueChange={setCustomerId}>
                  <Select.Trigger
                    placeholder={customersQuery.isLoading ? 'Loading customers...' : 'Select customer'}
                    aria-labelledby="contract-create-customer-label"
                    aria-invalid={basicsAttempted && !!customerError}
                    aria-describedby={basicsAttempted && customerError ? 'contract-create-customer-error' : undefined}
                  />
                  <Select.Content>
                    {canReadCustomers ? (customersQuery.data?.items ?? []).map((customer) => (
                      <Select.Item key={customer.id} value={customer.id}>
                        {customer.name} ({customer.id})
                      </Select.Item>
                    )) : null}
                  </Select.Content>
                </Select.Root>
                {basicsAttempted ? (
                  <FieldErrorText id="contract-create-customer-error" message={customerError} />
                ) : null}

                <ContractSourceSelector value={sourceIds} onChange={setSourceIds} />
                {basicsAttempted ? <FieldErrorText id="contract-create-source-error" message={sourceError} /> : null}

                <Text as="label" htmlFor="contract-create-name" size="2" color="gray">Contract name (optional)</Text>
                <TextField.Root
                  id="contract-create-name"
                  placeholder="Optional friendly name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />

                <Text id="contract-create-event-type-label" size="2" color="gray">Event type</Text>
                <Select.Root value={selectedEventType} onValueChange={setSelectedEventType}>
                  <Select.Trigger
                    placeholder={eventDefinitionsQuery.isLoading ? 'Loading event types...' : 'Select event type'}
                    aria-labelledby="contract-create-event-type-label"
                    aria-invalid={basicsAttempted && !!eventTypeError}
                    aria-describedby={basicsAttempted && eventTypeError ? 'contract-create-event-type-error' : undefined}
                  />
                  <Select.Content>
                    {canReadEventDefinitions ? (eventDefinitionsQuery.data?.items ?? []).map((definition) => (
                      <Select.Item key={definition.eventType} value={definition.eventType}>
                        {definition.eventType}
                      </Select.Item>
                    )) : null}
                    <Select.Item value={createNewEventTypeValue}>Create new event type</Select.Item>
                  </Select.Content>
                </Select.Root>
                {selectedEventType === createNewEventTypeValue ? (
                  <TextField.Root
                    placeholder="orders.created"
                    aria-label="New event type"
                    value={newEventType}
                    onChange={(event) => setNewEventType(event.target.value)}
                    aria-invalid={basicsAttempted && !!eventTypeError}
                  />
                ) : null}
                {basicsAttempted ? (
                  <FieldErrorText id="contract-create-event-type-error" message={eventTypeError} />
                ) : null}
              </>
            ) : null}

            {step === 1 ? (
              <>
                <ContractDestinationSelector value={destinationIds} onChange={setDestinationIds} />
                {destinationAttempted ? (
                  <FieldErrorText id="contract-create-destination-error" message={destinationError} />
                ) : null}
              </>
            ) : null}

            {step === 2 ? (
              <>
                <Heading size="4">Review contract</Heading>
                <ContractOptionsForm value={optionsDraft} onChange={setOptionsDraft} eventTypes={eventType ? [eventType] : []} showValidation={optionsValidation.attempted} disabled={createMutation.isPending} onRawEditingChange={(key, editing) => setRawEditing((current) => editing ? [...current.filter((item) => item !== key), key] : current.filter((item) => item !== key))} />
                <Text size="2" color="gray">Confirm the details before creating the contract. Credentials are not shown.</Text>
                <Card>
                  <Flex direction="column" gap="2">
                    <Text size="2"><strong>Customer:</strong> {customersQuery.data?.items.find((item) => item.id === customerId)?.name ?? customerId}</Text>
                    <Text size="2"><strong>Sources:</strong> {sourceIds.join(', ')}</Text>
                    <Text size="2"><strong>Name:</strong> {name.trim() || '—'}</Text>
                    <Text size="2"><strong>Event type:</strong> {eventType}</Text>
                    <Text size="2"><strong>Destinations:</strong> {destinationIds.join(', ')}</Text>
                  </Flex>
                </Card>
                <FormErrorText
                  id="contract-create-submit-error"
                  message={submitError ?? (createMutation.isError
                    ? getApiErrorMessage(createMutation.error, 'Unable to create contract.')
                    : null)}
                />
              </>
            ) : null}

            <Flex gap="2" justify="end" mt="3" className="configuration-actions">
              <Button asChild variant="soft" color="gray" type="button">
                <NavLink to="/contracts">Cancel</NavLink>
              </Button>
              {step > 0 ? (
                <Button type="button" variant="soft" color="gray" disabled={rawEditing.length > 0} onClick={() => {
                  setSubmitError(null)
                  setStep(step === 2 ? 1 : 0)
                }}>
                  Back
                </Button>
              ) : null}
              <Button type="submit" disabled={createMutation.isPending || (step === 2 && rawEditing.length > 0)}>
                {createMutation.isPending ? 'Creating…' : step === 2 ? 'Create contract' : 'Next'}
              </Button>
            </Flex>
          </Flex>
        </form>
      </Card>
    </Flex>
  )
}
