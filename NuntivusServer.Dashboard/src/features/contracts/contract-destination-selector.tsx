import { Button, Checkbox, Flex, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { FormDialog } from '../../components/dialogs/form-dialog'
import { TrashIcon } from '../../components/icons/nav-icons'
import { JsonViewer } from '../../components/json/json-viewer'
import { DetailIndicator, NavigableTableRow, TableIconButton } from '../../components/tables/table-actions'
import { useDestinationsQuery } from '../destinations/use-destinations'

export function ContractDestinationSelector({ value, onChange, disabled, refreshIds, onRefreshChange, detached, retainedDetachedIds, onRetainedDetachedChange, table = false }: {
  value: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
  refreshIds?: string[]
  onRefreshChange?: (ids: string[]) => void
  detached?: { id: string; type: string; configuration: string }[]
  retainedDetachedIds?: string[]
  onRetainedDetachedChange?: (ids: string[]) => void
  table?: boolean
}) {
  const query = useDestinationsQuery()
  const [viewedDetachedId, setViewedDetachedId] = useState<string | null>(null)
  const viewedDetached = detached?.find(item => item.id === viewedDetachedId)
  let viewedConfiguration: unknown = null
  if (viewedDetached) {
    try {
      viewedConfiguration = JSON.parse(viewedDetached.configuration)
    } catch {
      viewedConfiguration = viewedDetached.configuration
    }
  }
  const toggle = (ids: string[] | undefined, id: string, checked: boolean, update: ((ids: string[]) => void) | undefined) => update?.(checked ? [...(ids ?? []), id] : (ids ?? []).filter(item => item !== id))
  if (table) return <>
    <div className="contract-selection-table-wrap">
    {query.isLoading && <Text size="2" color="gray">Loading Destinations...</Text>}
    {query.isError && <Text size="2" color="red">Unable to load Destinations.</Text>}
    <table className="contract-selection-table">
      <thead><tr><th scope="col">Destination name</th><th scope="col">Use latest settings</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
      <tbody>{value.map(id => {
        const item = query.data?.find(destination => destination.id === id)
        return <NavigableTableRow key={id} unstyled to={item ? `/destinations/${encodeURIComponent(id)}` : undefined} label={`Destination ${item?.name ?? id}`}>
          <td>{item?.name ?? id}</td>
          <td>{item && <label className="contract-selection-check"><Checkbox checked={refreshIds?.includes(id) ?? false} disabled={disabled || !item.enabled} onCheckedChange={checked => toggle(refreshIds, id, checked === true, onRefreshChange)} aria-label={`Use latest settings for ${item.name}`} /> Update on save</label>}</td>
          <td><span className={`contract-selection-status ${item?.enabled ? 'is-active' : ''}`}>{item ? item.enabled ? 'Active' : 'Inactive' : 'Unavailable'}</span></td>
          <td><span className="contract-selection-actions">
            <TableIconButton label={`Remove ${item?.name ?? id} from contract`} disabled={disabled} onClick={() => onChange(value.filter(destinationId => destinationId !== id))} color="red"><TrashIcon /></TableIconButton>{item && <DetailIndicator />}</span></td>
        </NavigableTableRow>
      })}{detached?.filter(item => retainedDetachedIds?.includes(item.id) ?? true).map(item => <tr key={item.id}>
        <td>{item.type} destination (no longer in Destinations)</td><td>—</td>
        <td><span className="contract-selection-status">Unlinked</span></td><td><span className="contract-selection-actions"><Button type="button" variant="ghost" size="2" onClick={() => setViewedDetachedId(item.id)} aria-label={`View saved ${item.type} destination settings`}>View</Button>
          <TableIconButton label={`Remove unlinked ${item.type} destination from contract`} disabled={disabled} onClick={() => onRetainedDetachedChange?.((retainedDetachedIds ?? []).filter(id => id !== item.id))} color="red"><TrashIcon /></TableIconButton></span></td>
      </tr>)}{value.length === 0 && !retainedDetachedIds?.length && <tr><td colSpan={4} className="contract-selection-empty"><Text size="2" color="gray">No destinations selected. Use Add destination to choose one.</Text></td></tr>}</tbody>
    </table>
    </div>
    {(value.length > 0 || !!retainedDetachedIds?.length) && <Text size="1" color="gray">Linked destinations keep their saved settings. Select “Update on save” to use the latest settings from Destinations.</Text>}
    <FormDialog open={viewedDetached !== undefined} onOpenChange={open => { if (!open) setViewedDetachedId(null) }} title="Saved destination settings" description="This destination is no longer in Destinations. The contract retains these settings until you remove it." maxWidth="720px">
      {viewedDetached && <Flex direction="column" gap="3">
        <Text size="2">Type: {viewedDetached.type}</Text>
        <JsonViewer value={viewedConfiguration} copyLabel="Copy settings" />
      </Flex>}
    </FormDialog>
  </>
  return <Flex direction="column" gap="2">
    <Text size="2" weight="medium">Destinations (each matching event goes to all selected destinations)</Text>
    {query.isLoading && <Text size="2">Loading Destinations...</Text>}
    {query.isError && <Text size="2" color="red">Unable to load Destinations.</Text>}
    {query.data?.filter(item => item.configured).map(item => {
      const selected = value.includes(item.id)
      return <Flex key={item.id} align="center" gap="2">
        <Checkbox checked={selected} disabled={disabled || (!item.enabled && !selected)} onCheckedChange={checked =>
          onChange(checked ? [...value, item.id] : value.filter(id => id !== item.id))} aria-label={`Select ${item.name}`} />
        <Text size="2">{item.name} ({item.type}){item.enabled ? '' : ' — disabled'}</Text>
        {selected && onRefreshChange && <label><Checkbox checked={refreshIds?.includes(item.id) ?? false}
          disabled={disabled || !item.enabled} onCheckedChange={checked => onRefreshChange(checked
            ? [...(refreshIds ?? []), item.id] : (refreshIds ?? []).filter(id => id !== item.id))} /> Refresh snapshot</label>}
      </Flex>
    })}
    {detached?.map(item => <Flex key={item.id} align="center" gap="2">
      <Checkbox checked={retainedDetachedIds?.includes(item.id) ?? false} disabled={disabled}
        onCheckedChange={checked => onRetainedDetachedChange?.(checked
          ? [...(retainedDetachedIds ?? []), item.id] : (retainedDetachedIds ?? []).filter(id => id !== item.id))}
        aria-label={`Keep detached ${item.type} snapshot`} />
      <Text size="2">{item.type} — detached snapshot (keep or remove)</Text>
    </Flex>)}
    {value.some(id => !query.data?.some(item => item.id === id)) && <Text size="2" color="gray">Detached snapshots remain active until removed.</Text>}
    <Text size="1" color="gray">Manage shared configurations in <NavLink to="/destinations">Destinations</NavLink>. Refresh only when you want this contract to adopt changes.</Text>
  </Flex>
}
