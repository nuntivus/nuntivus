using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;

namespace KairoServer.Application.Auditing
{
    public sealed class AuditLogQueryService : IAuditLogQueryService
    {
        public const int MaxPageSize = 100;

        private readonly IAuditLogRepository _auditLogs;

        public AuditLogQueryService(IAuditLogRepository auditLogs)
        {
            _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        }

        public async Task<Result<PagedResult<AuditLogListItemView>>> ListAsync(
            AuditLogListQuery query,
            CancellationToken cancellationToken)
        {
            if (query is null)
                return Result.Failure<PagedResult<AuditLogListItemView>>(Error.Validation("query is required."));

            if (query.Page < 1)
                return Result.Failure<PagedResult<AuditLogListItemView>>(Error.Validation("page must be greater than or equal to 1."));

            if (query.PageSize < 1 || query.PageSize > MaxPageSize)
            {
                return Result.Failure<PagedResult<AuditLogListItemView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));
            }

            if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
            {
                return Result.Failure<PagedResult<AuditLogListItemView>>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (!string.IsNullOrWhiteSpace(query.SortDirection)
                && !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PagedResult<AuditLogListItemView>>(
                    Error.Validation("sortDirection must be one of: asc, desc."));
            }

            return await _auditLogs.ListAsync(query, cancellationToken);
        }
    }
}