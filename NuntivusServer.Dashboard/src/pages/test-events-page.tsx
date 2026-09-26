import { Button, Card, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { NavLink, useSearchParams } from 'react-router-dom'
import { ContractTestEventForm } from '../features/contracts/contract-test-event-form'
import { useContractDetailQuery, useContractsQuery } from '../features/contracts/use-contracts'
import { getApiErrorMessage } from '../services/api/api-client'

export function TestEventsPage() {
  const [params, setParams] = useSearchParams()
  const contractId = params.get('contractId') ?? ''
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const contractsQuery = useContractsQuery({ page: 1, pageSize: 100, search, sortBy: 'updatedAt', sortDirection: 'desc' })
  const detailQuery = useContractDetailQuery(contractId || undefined)
  const selected = detailQuery.data?.contract
  const contracts = contractsQuery.data?.items ?? []
  const selectedIsListed = contracts.some((contract) => contract.id === contractId)

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSearch(searchInput.trim())
  }

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Test events</h1>
          <p className="page-subtitle">Send a controlled event to one contract destination.</p>
        </div>
      </div>
      <Card className="operations-filter-card">
        <Flex direction="column" gap="3">
          <Text size="2" color="gray">
            Select a contract to preview a test event or send it only to that contract’s destination.
            Sending performs a real delivery, not a simulation.
          </Text>
          <form onSubmit={handleSearch}>
            <Flex gap="2" wrap="wrap">
              <TextField.Root
                aria-label="Search contracts"
                placeholder="Search contracts by name or ID"
                value={searchInput}
                onChange={(event) => setSearchInput(event.target.value)}
              />
              <Button type="submit" variant="soft">Search</Button>
            </Flex>
          </form>
          <Text size="2" as="label" htmlFor="test-contract">Contract</Text>
          <Select.Root value={contractId} onValueChange={(value) => setParams({ contractId: value })}>
            <Select.Trigger id="test-contract" placeholder="Choose contract" />
            <Select.Content>
              {selected && !selectedIsListed ? (
                <Select.Item value={selected.id}>{selected.name ?? selected.id} ({selected.customerId})</Select.Item>
              ) : null}
              {contracts.map((contract) => (
                <Select.Item key={contract.id} value={contract.id}>
                  {contract.name ?? contract.id} ({contract.customerId})
                </Select.Item>
              ))}
            </Select.Content>
          </Select.Root>
          {contractsQuery.isError ? (
            <Text size="2" color="red">{getApiErrorMessage(contractsQuery.error, 'Unable to load contracts.')}</Text>
          ) : contractsQuery.isLoading ? (
            <Text size="2" color="gray">Loading contracts...</Text>
          ) : contracts.length === 0 && !selected ? (
            <Text size="2" color="gray">No contracts found. <NavLink to="/contracts">View contracts</NavLink></Text>
          ) : null}
        </Flex>
      </Card>
      {contractId && detailQuery.isLoading ? <Text size="2" color="gray">Loading selected contract...</Text> : null}
      {contractId && detailQuery.isError ? (
        <Text size="2" color="red">{getApiErrorMessage(detailQuery.error, 'Unable to load selected contract.')}</Text>
      ) : null}
      {selected ? <ContractTestEventForm key={selected.id} contract={selected} /> : null}
    </Flex>
  )
}
