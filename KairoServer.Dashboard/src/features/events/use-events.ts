import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQueryKeys } from '../shared/query-invalidation'
import {
  getEventById,
  listEvents,
  replayEvent,
  type EventListItem,
  type EventListQuery,
  type ReplayEventRequest,
} from '../../services/api/events-api'

export function useEventsQuery(query: EventListQuery) {
  return useQuery({
    queryKey: ['events', query],
    queryFn: ({ signal }) => listEvents(query, { signal }),
  })
}

export function useReplayedEventDeliveriesQuery(replayedEventId: string | undefined) {
  return useQuery({
    queryKey: ['events', 'replay', replayedEventId, 'deliveries'],
    queryFn: ({ signal }) => getEventById(replayedEventId ?? '', { signal }),
    enabled: !!replayedEventId,
    select: (event: EventListItem) => event.deliveries,
  })
}

export function useEventDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['events', 'detail', id],
    queryFn: ({ signal }) => getEventById(id ?? '', { signal }),
    enabled: !!id,
  })
}

export function useReplayEventMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ReplayEventRequest) => replayEvent(id, request),
    onSuccess: async () => {
      await invalidateQueryKeys(queryClient, [
        ['events'],
        ['events', 'detail', id],
      ])
    },
  })
}
