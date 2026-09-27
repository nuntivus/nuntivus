import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  activateSchemaVersion,
  createEventDefinition,
  createSchemaDraft,
  deprecateSchemaVersion,
  getEventDefinition,
  getSchemaVersion,
  listContractsRelatedToEventType,
  listEventDefinitions,
  listSchemaVersions,
  updateEventDefinition,
  type CreateEventDefinitionRequest,
  type CreateSchemaVersionRequest,
  type EventDefinitionListQuery,
  type UpdateEventDefinitionRequest,
} from '../../services/api/event-definitions-api'

export function useEventDefinitionsQuery(query: EventDefinitionListQuery) {
  return useQuery({
    queryKey: ['event-definitions', query],
    queryFn: ({ signal }) => listEventDefinitions(query, { signal }),
  })
}

export function useEventDefinitionDetailQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType],
    queryFn: ({ signal }) => getEventDefinition(eventType ?? '', { signal }),
    enabled: !!eventType,
  })
}

export function useEventSchemaVersionsQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'schemas'],
    queryFn: ({ signal }) => listSchemaVersions(eventType ?? '', 1, 20, { signal }),
    enabled: !!eventType,
  })
}

export function useEventSchemaVersionQuery(eventType: string | undefined, version: number | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'schema', version],
    queryFn: ({ signal }) => getSchemaVersion(eventType ?? '', version ?? 0, { signal }),
    enabled: !!eventType && typeof version === 'number' && version > 0,
  })
}

export function useRelatedContractsQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'contracts'],
    queryFn: async ({ signal }) => {
      const response = await listContractsRelatedToEventType(eventType ?? '', { signal })
      return response.items
    },
    enabled: !!eventType,
  })
}

export function useCreateEventDefinitionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateEventDefinitionRequest) => createEventDefinition(request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [['event-definitions']])
    },
  })
}

export function useUpdateEventDefinitionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateEventDefinitionRequest) =>
      updateEventDefinition(eventType, request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['event-definitions'],
        ['event-definitions', 'detail', eventType],
      ])
    },
  })
}

export function useCreateSchemaDraftMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateSchemaVersionRequest) => createSchemaDraft(eventType, request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['event-definitions'],
        ['event-definitions', 'detail', eventType, 'schemas'],
      ])
    },
  })
}

export function useActivateSchemaVersionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (version: number) => activateSchemaVersion(eventType, version),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['event-definitions'],
        ['event-definitions', 'detail', eventType, 'schemas'],
      ])
    },
  })
}

export function useDeprecateSchemaVersionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (version: number) => deprecateSchemaVersion(eventType, version),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['event-definitions'],
        ['event-definitions', 'detail', eventType, 'schemas'],
      ])
    },
  })
}
