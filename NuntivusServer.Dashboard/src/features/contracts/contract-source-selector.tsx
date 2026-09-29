import { Checkbox, Flex, Link, Text } from '@radix-ui/themes'
import { NavLink } from 'react-router-dom'
import { useSourcesQuery } from '../sources/use-sources'

export function ContractSourceSelector({ value, onChange, disabled = false }: {
  value: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
}) {
  const sources = useSourcesQuery()
  return <Flex direction="column" gap="2">
    <Text size="2" weight="medium">Sources (select at least one)</Text>
    {sources.isLoading && <Text size="2" color="gray">Loading Sources...</Text>}
    {sources.isError && <Text size="2" color="red">Unable to load Sources. Try again before saving.</Text>}
    {sources.data?.length === 0 && <Text size="2" color="gray">No Sources configured. <Link asChild><NavLink to="/sources">Manage Sources</NavLink></Link></Text>}
    {sources.data?.map((source) => <label key={source.id}>
      <Flex align="center" gap="2"><Checkbox checked={value.includes(source.id)} disabled={disabled || (!source.enabled && !value.includes(source.id))}
        onCheckedChange={(checked) => onChange(checked === true ? [...value, source.id] : value.filter((id) => id !== source.id))} />
        <Text size="2">{source.name} ({source.type}{source.enabled ? '' : ', disabled'})</Text>
      </Flex>
    </label>)}
    {value.some((id) => !sources.data?.some((source) => source.id === id)) && !sources.isLoading && <Text size="2" color="red">One or more selected Sources are unavailable.</Text>}
  </Flex>
}
