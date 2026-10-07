import { Checkbox, Flex, Text } from '@radix-ui/themes'
import { NavLink } from 'react-router-dom'
import { useDestinationsQuery } from '../destinations/use-destinations'

export function ContractDestinationSelector({ value, onChange, disabled, refreshIds, onRefreshChange, detached, retainedDetachedIds, onRetainedDetachedChange }: {
  value: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
  refreshIds?: string[]
  onRefreshChange?: (ids: string[]) => void
  detached?: { id: string; type: string }[]
  retainedDetachedIds?: string[]
  onRetainedDetachedChange?: (ids: string[]) => void
}) {
  const query = useDestinationsQuery()
  return <Flex direction="column" gap="2">
    <Text size="2" weight="medium">Destinations (each matching event goes to all selected destinations)</Text>
    {query.isLoading && <Text size="2">Loading Destinations...</Text>}
    {query.isError && <Text size="2" color="red">Unable to load Destinations.</Text>}
    {query.data?.map(item => {
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
