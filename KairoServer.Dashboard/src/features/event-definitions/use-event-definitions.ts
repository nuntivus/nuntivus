import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
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
  updateEventDefinitionDescription,
  type CreateEventDefinitionRequest,
  type CreateSchemaVersionRequest,
  type EventDefinitionListQuery,
  type UpdateEventDefinitionDescriptionRequest,
} from '../../services/api/event-definitions-api'

export function useEventDefinitionsQuery(query: EventDefinitionListQuery) {
  return useQuery({
    queryKey: ['event-definitions', query],
    queryFn: () => listEventDefinitions(query),
  })
}

export function useEventDefinitionDetailQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType],
    queryFn: () => getEventDefinition(eventType ?? ''),
    enabled: !!eventType,
  })
}

export function useEventSchemaVersionsQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'schemas'],
    queryFn: () => listSchemaVersions(eventType ?? ''),
    enabled: !!eventType,
  })
}

export function useEventSchemaVersionQuery(eventType: string | undefined, version: number | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'schema', version],
    queryFn: () => getSchemaVersion(eventType ?? '', version ?? 0),
    enabled: !!eventType && typeof version === 'number' && version > 0,
  })
}

export function useRelatedContractsQuery(eventType: string | undefined) {
  return useQuery({
    queryKey: ['event-definitions', 'detail', eventType, 'contracts'],
    queryFn: async () => {
      const response = await listContractsRelatedToEventType(eventType ?? '')
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
      await queryClient.invalidateQueries({ queryKey: ['event-definitions'] })
    },
  })
}

export function useUpdateEventDefinitionDescriptionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateEventDefinitionDescriptionRequest) =>
      updateEventDefinitionDescription(eventType, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['event-definitions'] }),
        queryClient.invalidateQueries({
          queryKey: ['event-definitions', 'detail', eventType],
        }),
      ])
    },
  })
}

export function useCreateSchemaDraftMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateSchemaVersionRequest) => createSchemaDraft(eventType, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['event-definitions'] }),
        queryClient.invalidateQueries({
          queryKey: ['event-definitions', 'detail', eventType, 'schemas'],
        }),
      ])
    },
  })
}

export function useActivateSchemaVersionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (version: number) => activateSchemaVersion(eventType, version),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['event-definitions'] }),
        queryClient.invalidateQueries({
          queryKey: ['event-definitions', 'detail', eventType, 'schemas'],
        }),
      ])
    },
  })
}

export function useDeprecateSchemaVersionMutation(eventType: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (version: number) => deprecateSchemaVersion(eventType, version),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['event-definitions'] }),
        queryClient.invalidateQueries({
          queryKey: ['event-definitions', 'detail', eventType, 'schemas'],
        }),
      ])
    },
  })
}
