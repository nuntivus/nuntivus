import { Badge, Button, Flex, Heading, Table, Text } from '@radix-ui/themes'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { FormErrorText } from '../../components/forms/form-feedback'
import { getApiErrorMessage } from '../../services/api/api-client'
import { getSchemaImpact, type ChangeImpactReport } from '../../services/api/change-impact-api'

export function ChangeImpactResults({ report }: { report: ChangeImpactReport }) {
  return <Flex direction="column" gap="2">
    <Text size="2" color="gray">{report.evidenceNotice}</Text>
    <Text size="2">{report.totalContracts} potentially affected contracts</Text>
    <Table.Root>
      <Table.Header><Table.Row><Table.ColumnHeaderCell>Contract / customer</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Assessment</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Bindings</Table.ColumnHeaderCell><Table.ColumnHeaderCell>Samples</Table.ColumnHeaderCell></Table.Row></Table.Header>
      <Table.Body>{report.contracts.map(contract => <Table.Row key={contract.contractId}>
        <Table.RowHeaderCell><NavLink to={`/contracts/${contract.contractId}`}>{contract.name ?? contract.contractId}</NavLink><br /><Text size="1">{contract.customerId}{contract.enabled ? '' : ' · disabled'}</Text></Table.RowHeaderCell>
        <Table.Cell><Badge color={contract.status === 'Compatible' ? 'green' : contract.status === 'Breaking' ? 'red' : 'amber'}>{contract.status === 'ReviewRequired' ? 'Review required' : contract.status}</Badge><Text as="p" size="1">{contract.reason}</Text></Table.Cell>
        <Table.Cell>{contract.bindings.map(binding => <Text as="p" size="1" key={binding.id}>{binding.destinationType} · {binding.id}{binding.deliveryEnabled ? '' : ' · disabled'}</Text>)}</Table.Cell>
        <Table.Cell>{contract.samples.length ? contract.samples.map(sample => <Text as="p" size="1" key={sample.eventId}><NavLink to={`/events/${sample.eventId}`}>{sample.accepted ? 'Accepted sample' : 'Rejected sample'}</NavLink>{sample.findings.length ? ` · ${sample.findings.join(', ')}` : ''}</Text>) : <Text size="1">No applicable samples</Text>}</Table.Cell>
      </Table.Row>)}</Table.Body>
    </Table.Root>
  </Flex>
}

export function SchemaChangeImpactPanel({ eventType, version }: { eventType: string; version: number }) {
  const [page, setPage] = useState(1)
  const [enabled, setEnabled] = useState(false)
  const impact = useQuery({ queryKey: ['event-definitions', eventType, 'impact', version, page],
    queryFn: ({ signal }) => getSchemaImpact(eventType, version, page, { signal }), enabled })
  return <Flex direction="column" gap="3">
    <Heading size="3">Change impact</Heading>
    <Button type="button" variant="soft" disabled={impact.isFetching} onClick={() => { setEnabled(true); if (enabled) void impact.refetch() }} style={{ alignSelf: 'flex-start' }}>{impact.isFetching ? 'Analyzing...' : 'Analyze affected contracts'}</Button>
    {impact.isError ? <FormErrorText message={getApiErrorMessage(impact.error, 'Unable to analyze change impact.')} /> : null}
    {impact.data ? <>
      <ChangeImpactResults report={impact.data} />
      <Flex gap="2"><Button type="button" variant="soft" disabled={page === 1 || impact.isFetching} onClick={() => setPage(page - 1)}>Previous</Button><Text>Page {page}</Text><Button type="button" variant="soft" disabled={page * impact.data.pageSize >= impact.data.totalContracts || impact.isFetching} onClick={() => setPage(page + 1)}>Next</Button></Flex>
    </> : null}
  </Flex>
}
