import { NavLink } from 'react-router-dom'
import { useEntityLabel, type EntityKind } from './use-entity-label'

const entityPaths: Record<EntityKind, string> = {
  customer: 'customers',
  contract: 'contracts',
  event: 'events',
  source: 'sources',
  destination: 'destinations',
}

export function EntityReference({ kind, id, label }: {
  kind: EntityKind
  id: string | null | undefined
  label?: string | null
}) {
  const name = useEntityLabel(kind, id, label)
  if (!id) return '-'

  if (!name && kind === 'source') {
    return <span title={`Source ID: ${id}`}>{id}</span>
  }

  return (
    <NavLink to={`/${entityPaths[kind]}/${encodeURIComponent(id)}`} title={`${kind} ID: ${id}`}>
      {name || id}
    </NavLink>
  )
}
