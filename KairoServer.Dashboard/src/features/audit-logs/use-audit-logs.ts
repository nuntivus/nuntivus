import { useQuery } from '@tanstack/react-query'
import {
  listAuditLogs,
  type AuditLogListQuery,
} from '../../services/api/audit-logs-api'

export function useAuditLogsQuery(query: AuditLogListQuery) {
  return useQuery({
    queryKey: ['admin', 'audit-logs', query],
    queryFn: ({ signal }) => listAuditLogs(query, { signal }),
  })
}