import { Checkbox, Flex, Link, Text } from '@radix-ui/themes'
import { NavLink } from 'react-router-dom'
import { TrashIcon } from '../../components/icons/nav-icons'
import { DetailIndicator, NavigableTableRow, TableIconButton } from '../../components/tables/table-actions'
import { useSourcesQuery } from '../sources/use-sources'

export function ContractSourceSelector({ value, onChange, disabled = false, table = false }: {
  value: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
  table?: boolean
}) {
  const sources = useSourcesQuery()
  const toggle = (id: string, checked: boolean) => onChange(checked ? [...value, id] : value.filter((item) => item !== id))
  if (table) return <div className="contract-selection-table-wrap">
    {sources.isLoading && <Text size="2" color="gray">Loading Sources...</Text>}
    {sources.isError && <Text size="2" color="red">Unable to load Sources. Try again before saving.</Text>}
    <table className="contract-selection-table">
      <thead><tr><th scope="col">Source name</th><th scope="col">Type</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
      <tbody>{value.map((id) => {
        const source = sources.data?.find((item) => item.id === id)
        return <NavigableTableRow key={id} unstyled to={source ? `/sources/${encodeURIComponent(id)}` : undefined} label={`Source ${source?.name ?? id}`}>
          <td>{source?.name ?? id}</td><td>{source?.type ?? '—'}</td><td><span className={`contract-selection-status ${source?.enabled ? 'is-active' : ''}`}>{source ? source.enabled ? 'Active' : 'Inactive' : 'Unavailable'}</span></td>
          <td><span className="contract-selection-actions">
            <TableIconButton label={`Remove ${source?.name ?? id} from contract`} disabled={disabled} onClick={() => toggle(id, false)} color="red"><TrashIcon /></TableIconButton>{source && <DetailIndicator />}</span></td>
        </NavigableTableRow>
      })}{value.length === 0 && <tr><td colSpan={4} className="contract-selection-empty"><Text size="2" color="gray">No sources selected. Use Add source to choose one.</Text></td></tr>}</tbody>
    </table>
    {value.some((id) => !sources.data?.some((source) => source.id === id)) && !sources.isLoading && <Text size="2" color="red">One or more selected Sources are unavailable.</Text>}
  </div>
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
