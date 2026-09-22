import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
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
    queryFn: () => listEvents(query),
  })
}

export function useReplayedEventDeliveriesQuery(replayedEventId: string | undefined) {
  return useQuery({
    queryKey: ['events', 'replay', replayedEventId, 'deliveries'],
    queryFn: () => getEventById(replayedEventId ?? ''),
    enabled: !!replayedEventId,
    select: (event: EventListItem) => event.deliveries,
  })
}

export function useEventDetailQuery(id: string | undefined) {
  return useQuery({
    queryKey: ['events', 'detail', id],
    queryFn: () => getEventById(id ?? ''),
    enabled: !!id,
  })
}

export function useReplayEventMutation(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ReplayEventRequest) => replayEvent(id, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['events'] }),
        queryClient.invalidateQueries({ queryKey: ['events', 'detail', id] }),
      ])
    },
  })
}
