import { Background, Controls, Handle, MiniMap, Position, ReactFlow, type NodeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Badge, Button, Card, Flex, Heading, Select, Text, TextField } from '@radix-ui/themes'
import { useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { buildRoutingGraph, traceRoutingFlow, type RouteNodeData } from '../features/routing-map/routing-graph'
import { useRoutingMapQuery } from '../features/routing-map/use-routing-map'
import { getApiErrorMessage } from '../services/api/api-client'

function RouteNode({ data }: NodeProps) {
  const route = data as RouteNodeData
  const ports = (ids: string[], type: 'source' | 'target', position: Position) => ids.map((id, index) => (
    <Handle key={id} id={`${type === 'source' ? 'out' : 'in'}:${id}`} type={type} position={position}
      style={{ top: `${((index + 1) / (ids.length + 1)) * 100}%` }} />
  ))
  return (
    <div className={`routing-node routing-node-${route.kind} routing-node-${route.state}`}>
      {ports(route.inputPorts ?? [], 'target', Position.Left)}
      <span className="routing-node-heading"><span className="routing-node-symbol" aria-hidden="true">{route.kind === 'source' ? '⇥' : route.kind === 'contract' ? '◇' : '◎'}</span><span className="routing-node-kind">{route.kind}</span></span>
      <strong title={route.label}>{route.label}</strong>
      <span className="routing-node-subtitle" title={route.subtitle}>{route.subtitle}</span>
      <Badge color={route.state === 'enabled' ? 'green' : route.state === 'missing' ? 'red' : 'gray'} variant="soft">{route.state}</Badge>
      {ports(route.outputPorts ?? [], 'source', Position.Right)}
    </div>
  )
}

const nodeTypes = { route: RouteNode }

export function RoutingMapPage() {
  const query = useRoutingMapQuery()
  const [search, setSearch] = useState('')
  const [customer, setCustomer] = useState('all')
  const [source, setSource] = useState('all')
  const [destination, setDestination] = useState('all')
  const [eventType, setEventType] = useState('all')
  const [enabled, setEnabled] = useState('all')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const data = query.data
  const customers = useMemo(() => [...new Set(data?.contracts.map((contract) => contract.customerId) ?? [])].sort(), [data])
  const eventTypes = useMemo(() => [...new Set(data?.contracts.flatMap((contract) => contract.eventTypes ?? []) ?? [])].sort(), [data])
  const filtered = useMemo(() => (data?.contracts ?? []).filter((contract) => {
    const term = search.trim().toLowerCase()
    const sourceNames = (contract.sourceIds ?? []).map((id) => data?.sources.find((item) => item.id === id)?.name ?? id)
    const destinationNames = (contract.destinations ?? []).map((binding) => data?.destinations.find((item) => item.id === binding.destinationId)?.name ?? binding.destinationId ?? binding.type)
    return (!term || [contract.name, contract.id, contract.customerId, ...contract.eventTypes, ...sourceNames, ...destinationNames].some((value) => value?.toLowerCase().includes(term)))
      && (customer === 'all' || contract.customerId === customer)
      && (source === 'all' || contract.sourceIds?.includes(source))
      && (destination === 'all' || contract.destinations?.some((binding) => binding.destinationId === destination))
      && (eventType === 'all' || contract.eventTypes?.includes(eventType))
      && (enabled === 'all' || contract.enabled === (enabled === 'enabled'))
  }), [data, search, customer, source, destination, eventType, enabled])
  const filteredGraph = search || customer !== 'all' || source !== 'all' || destination !== 'all' || eventType !== 'all' || enabled !== 'all'
  const graph = useMemo(() => data ? buildRoutingGraph(data, filtered, !filteredGraph) : { nodes: [], edges: [] }, [data, filtered, filteredGraph])
  const activeSelectedId = graph.nodes.some((node) => node.id === selectedId) ? selectedId : null
  const selected = graph.nodes.find((node) => node.id === activeSelectedId)?.data
  const path = useMemo(() => activeSelectedId ? traceRoutingFlow(graph, activeSelectedId) : null, [graph, activeSelectedId])
  const nodes = useMemo(() => graph.nodes.map((node) => ({ ...node,
    className: path ? path.nodeIds.has(node.id) ? 'route-node-highlighted' : 'route-node-muted' : '',
  })), [graph, path])
  const edges = useMemo(() => graph.edges.map((edge) => ({ ...edge,
    className: `${edge.className ?? ''} ${path ? path.edgeIds.has(edge.id) ? 'route-edge-highlighted' : 'route-edge-muted' : ''}`.trim(),
    zIndex: path?.edgeIds.has(edge.id) ? 10 : 0,
  })), [graph, path])
  const pathNodes = path ? graph.nodes.filter((node) => path.nodeIds.has(node.id)) : []

  return (
    <Flex direction="column" gap="4" className="routing-map-page">
      <Flex align="center" justify="between" wrap="wrap" gap="2">
        <div><Heading size="6">Routing map</Heading><Text size="2" color="gray">Configured Source → Contract → Destination relationships. This is not live traffic.</Text></div>
        <Button variant="soft" onClick={() => void query.refetch()} disabled={query.isFetching}>Refresh configuration</Button>
      </Flex>
      {query.isPending ? <Card><Text>Loading routing configuration...</Text></Card> : query.isError ? (
        <Card><Text color="red">{getApiErrorMessage(query.error, 'Unable to load routing configuration.')}</Text></Card>
      ) : data ? <>
        {data.truncated ? <Card><Text color="amber">The routing map is incomplete: at least one resource type exceeds 1,000 records. Narrow the configuration or use the list pages for the full data.</Text></Card> : null}
        <Card><Flex gap="3" wrap="wrap" align="end" className="routing-map-filters">
          <label>Search<TextField.Root value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Name, ID, event type..." /></label>
          <label>Customer<Select.Root value={customer} onValueChange={setCustomer}><Select.Trigger /><Select.Content><Select.Item value="all">All customers</Select.Item>{customers.map((id) => <Select.Item key={id} value={id}>{id}</Select.Item>)}</Select.Content></Select.Root></label>
          <label>Source<Select.Root value={source} onValueChange={setSource}><Select.Trigger /><Select.Content><Select.Item value="all">All sources</Select.Item>{data.sources.map((item) => <Select.Item key={item.id} value={item.id}>{item.name}</Select.Item>)}</Select.Content></Select.Root></label>
          <label>Destination<Select.Root value={destination} onValueChange={setDestination}><Select.Trigger /><Select.Content><Select.Item value="all">All destinations</Select.Item>{data.destinations.map((item) => <Select.Item key={item.id} value={item.id}>{item.name}</Select.Item>)}</Select.Content></Select.Root></label>
          <label>Event type<Select.Root value={eventType} onValueChange={setEventType}><Select.Trigger /><Select.Content><Select.Item value="all">All event types</Select.Item>{eventTypes.map((item) => <Select.Item key={item} value={item}>{item}</Select.Item>)}</Select.Content></Select.Root></label>
          <label>State<Select.Root value={enabled} onValueChange={setEnabled}><Select.Trigger /><Select.Content><Select.Item value="all">All states</Select.Item><Select.Item value="enabled">Enabled contracts</Select.Item><Select.Item value="disabled">Disabled contracts</Select.Item></Select.Content></Select.Root></label>
        </Flex></Card>
        <Text size="2" color="gray">Showing {filtered.length} of {data.contracts.length} contracts. Select a node to trace its full flow. Dashed connections are disabled, paused, or unavailable. Unconnected resources remain visible when no filters are applied.</Text>
        <div className="routing-map-legend" aria-label="Graph legend"><span className="routing-legend-source">⇥ Source</span><span className="routing-legend-contract">◇ Contract · routing rules</span><span className="routing-legend-destination">◎ Destination</span><span className="routing-legend-line">━ Selected flow</span><span className="routing-legend-muted">┄ Paused or unavailable</span></div>
        <div className="routing-map-content">
          <div className="routing-map-canvas" role="region" aria-label="Configured routing graph">
            {graph.nodes.length ? <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes} nodesDraggable={false} nodesConnectable={false} elementsSelectable fitView minZoom={0.1} maxZoom={2} onNodeClick={(_, node) => setSelectedId(node.id)} onPaneClick={() => setSelectedId(null)}>
              <Background /><Controls /><MiniMap pannable zoomable />
            </ReactFlow> : <div className="routing-map-empty">{filteredGraph ? 'No contracts match these filters.' : 'No Sources, Contracts, or Destinations are configured.'}</div>}
          </div>
          <Card className="routing-map-inspector"><Flex justify="between" align="center"><Heading size="4">Flow explorer</Heading>{selected ? <Button size="1" variant="ghost" onClick={() => setSelectedId(null)}>Clear path</Button> : null}</Flex>{selected ? <Flex direction="column" gap="3">
            <Heading size="3">{selected.label}</Heading>
            <Badge color={selected.state === 'enabled' ? 'green' : 'gray'} variant="soft">{selected.state}</Badge>
            <Text size="2">{selected.subtitle}</Text>
            <div className="routing-path-summary" aria-label="Selected flow path">
              {(['source', 'contract', 'destination'] as const).map((kind) => <div key={kind} className={`routing-path-group routing-path-${kind}`}>
                <Text size="1" weight="bold">{kind === 'source' ? 'Sources' : kind === 'contract' ? 'Contracts' : 'Destinations'}</Text>
                {pathNodes.filter((node) => node.data.kind === kind).map((node) => <button key={node.id} type="button"
                  className={node.id === activeSelectedId ? 'routing-path-item active' : 'routing-path-item'}
                  onClick={() => setSelectedId(node.id)}>{node.data.label}</button>)}
                {pathNodes.every((node) => node.data.kind !== kind) ? <Text size="1" color="gray">None linked</Text> : null}
              </div>)}
            </div>
            {selected.details.map((detail) => <Text key={detail} size="2">{detail}</Text>)}
            {selected.resourceId ? <Text size="1" color="gray">ID: {selected.resourceId}</Text> : null}
            {selected.href ? <Button asChild variant="soft"><NavLink to={selected.href}>Open {selected.kind}</NavLink></Button> : null}
          </Flex> : <Text size="2" color="gray">Select a source, contract, or destination to reveal every connected route and inspect its configuration.</Text>}</Card>
        </div>
      </> : null}
    </Flex>
  )
}
