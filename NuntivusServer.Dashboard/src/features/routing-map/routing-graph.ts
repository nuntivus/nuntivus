import type { Edge, Node } from '@xyflow/react'
import type { RoutingMapData } from './use-routing-map'

export type RouteKind = 'source' | 'contract' | 'destination'
export interface RouteNodeData extends Record<string, unknown> {
  kind: RouteKind
  resourceId: string | null
  label: string
  subtitle: string
  state: 'enabled' | 'disabled' | 'missing' | 'detached'
  details: string[]
  href: string | null
  inputPorts?: string[]
  outputPorts?: string[]
}

export interface RoutingGraph {
  nodes: Node<RouteNodeData>[]
  edges: Edge[]
}

const nodeWidth = 260
const nodeHeight = 156
const rowSpacing = nodeHeight + 48
const columnSpacing = nodeWidth + 130

export function traceRoutingFlow(graph: RoutingGraph, selectedId: string) {
  const selected = graph.nodes.find((node) => node.id === selectedId)
  if (!selected) return { nodeIds: new Set<string>(), edgeIds: new Set<string>() }

  const kind = selected.data.kind
  const contractIds = kind === 'contract'
    ? new Set([selectedId])
    : new Set(graph.edges.filter((edge) => kind === 'source' ? edge.source === selectedId : edge.target === selectedId)
      .map((edge) => kind === 'source' ? edge.target : edge.source))
  const edges = graph.edges.filter((edge) => {
    if (kind === 'contract') return edge.source === selectedId || edge.target === selectedId
    if (kind === 'source') return (edge.source === selectedId && contractIds.has(edge.target)) || contractIds.has(edge.source)
    return (edge.target === selectedId && contractIds.has(edge.source)) || contractIds.has(edge.target)
  })
  return {
    nodeIds: new Set([selectedId, ...edges.flatMap((edge) => [edge.source, edge.target])]),
    edgeIds: new Set(edges.map((edge) => edge.id)),
  }
}

export function buildRoutingGraph(data: RoutingMapData, selectedContracts = data.contracts, includeOrphans = true): RoutingGraph {
  const nodes: Node<RouteNodeData>[] = []
  const edges: Edge[] = []
  const sourceMap = new Map(data.sources.map((source) => [source.id, source]))
  const destinationMap = new Map(data.destinations.map((destination) => [destination.id, destination]))
  const sourceIds = new Set<string>()
  const destinationIds = new Set<string>()

  for (const contract of selectedContracts) {
    for (const id of contract.sourceIds ?? []) sourceIds.add(id)
    for (const binding of contract.destinations ?? []) {
      if (binding.destinationId) destinationIds.add(binding.destinationId)
    }
  }
  if (includeOrphans) {
    for (const source of data.sources) sourceIds.add(source.id)
    for (const destination of data.destinations) destinationIds.add(destination.id)
  }

  const sources = [...sourceIds].sort((a, b) => (sourceMap.get(a)?.name ?? a).localeCompare(sourceMap.get(b)?.name ?? b))
  const destinations = [...destinationIds].sort((a, b) => (destinationMap.get(a)?.name ?? a).localeCompare(destinationMap.get(b)?.name ?? b))
  const contracts = [...selectedContracts].sort((a, b) => (a.name ?? a.id).localeCompare(b.name ?? b.id))

  sources.forEach((id, index) => {
    const source = sourceMap.get(id)
    nodes.push({ id: `source:${id}`, type: 'route', position: { x: 0, y: index * rowSpacing }, data: {
      kind: 'source', resourceId: id, label: source?.name ?? `Missing source (${id})`,
      subtitle: source?.type ?? 'Reference not found', state: !source ? 'missing' : source.enabled ? 'enabled' : 'disabled',
      details: source ? [source.description ?? 'No description'] : ['The referenced source is not available in loaded data.'],
      href: source ? `/sources/${encodeURIComponent(id)}` : null,
    } })
  })

  let detachedIndex = 0
  contracts.forEach((contract, index) => {
    const id = `contract:${contract.id}`
    const bindings = contract.destinations ?? []
    const options = [
      ['Schema targets', contract.schemaTargets?.length],
      ['Filters', !!contract.filter],
      ['Transformation', !!contract.transformation],
      ['Rate limit', !!contract.rateLimit],
      ['Ordering', !!contract.ordering],
    ].filter(([, value]) => !!value).map(([name]) => String(name))
    nodes.push({ id, type: 'route', position: { x: columnSpacing, y: index * rowSpacing }, data: {
      kind: 'contract', resourceId: contract.id, label: contract.name || `Contract ${contract.id.slice(0, 8)}`,
      subtitle: contract.eventTypes?.join(', ') || 'No event types', state: contract.enabled ? 'enabled' : 'disabled',
      details: [`Customer: ${contract.customerId}`, `Event types: ${contract.eventTypes?.join(', ') || 'None'}`,
        `Pipeline: ${options.join(' · ') || 'No advanced steps configured'}`, `${bindings.length} destination binding(s)`],
      href: `/contracts/${encodeURIComponent(contract.id)}`,
    } })
    for (const sourceId of contract.sourceIds ?? []) {
      edges.push({ id: `source:${sourceId}->${id}`, source: `source:${sourceId}`, target: id,
        type: 'default', animated: false, className: contract.enabled && sourceMap.get(sourceId)?.enabled ? '' : 'route-edge-disabled' })
    }
    for (const binding of bindings) {
      const target = binding.destinationId ? `destination:${binding.destinationId}` : `detached:${contract.id}:${binding.id}`
      if (!binding.destinationId) {
        nodes.push({ id: target, type: 'route', position: { x: columnSpacing * 2, y: (destinations.length + detachedIndex++) * rowSpacing }, data: {
          kind: 'destination', resourceId: null, label: `${binding.type} (detached)`, subtitle: 'No destination resource', state: 'detached',
          details: ['This binding has no linked destination resource.'], href: null,
        } })
      }
      edges.push({ id: `${id}->${target}:${binding.id}`, source: id, target, type: 'default',
        className: !contract.enabled || !binding.deliveryEnabled || !destinationMap.get(binding.destinationId ?? '')?.enabled ? 'route-edge-disabled' : '',
        label: !binding.deliveryEnabled ? 'Paused' : undefined })
    }
  })

  destinations.forEach((id, index) => {
    const destination = destinationMap.get(id)
    nodes.push({ id: `destination:${id}`, type: 'route', position: { x: columnSpacing * 2, y: index * rowSpacing }, data: {
      kind: 'destination', resourceId: id, label: destination?.name ?? `Missing destination (${id})`,
      subtitle: destination ? String(destination.type) : 'Reference not found',
      state: !destination ? 'missing' : destination.enabled ? 'enabled' : 'disabled',
      details: destination ? [destination.description ?? 'No description', destination.configured ? 'Configured' : 'Not configured'] : ['The referenced destination is not available in loaded data.'],
      href: destination ? `/destinations/${encodeURIComponent(id)}` : null,
    } })
  })

  for (const node of nodes) {
    node.data.inputPorts = edges.filter((edge) => edge.target === node.id).map((edge) => edge.id)
    node.data.outputPorts = edges.filter((edge) => edge.source === node.id).map((edge) => edge.id)
  }
  for (const edge of edges) {
    edge.sourceHandle = `out:${edge.id}`
    edge.targetHandle = `in:${edge.id}`
  }
  return { nodes, edges }
}
