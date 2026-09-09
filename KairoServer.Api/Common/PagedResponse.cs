using System.Collections.Generic;

namespace KairoServer.Api.Common
{
    /// <summary>
    /// HTTP wire shape for paged query results.
    /// </summary>
    public sealed record PagedResponse<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        long TotalCount,
        int TotalPages,
        bool HasPrevious,
        bool HasNext);
}
