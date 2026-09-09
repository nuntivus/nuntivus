using System;
using System.Collections.Generic;
using System.Linq;

namespace KairoServer.Common
{
    /// <summary>
    /// Immutable payload representing a single page of results.
    /// Wrapped by <see cref="Result{T}"/> so success/failure semantics stay uniform
    /// across all queries in the application.
    /// </summary>
    public sealed class PagedResult<T>
    {
        private PagedResult(
            IReadOnlyList<T> items,
            int page,
            int pageSize,
            long totalCount)
        {
            Items = items ?? [];
            Page = page;
            PageSize = pageSize;
            TotalCount = totalCount;
            TotalPages = pageSize <= 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);
        }

        public IReadOnlyList<T> Items { get; }

        public int Page { get; }

        public int PageSize { get; }

        public long TotalCount { get; }

        public int TotalPages { get; }

        public bool HasPrevious => Page > 1;

        public bool HasNext => Page < TotalPages;

        public static PagedResult<T> Create(
            IEnumerable<T> items,
            int page,
            int pageSize,
            long totalCount)
        {
            var materialized = items?.ToArray() ?? [];
            return new PagedResult<T>(materialized, page, pageSize, totalCount);
        }

        public static PagedResult<T> Empty(int page, int pageSize) =>
            new([], page, pageSize, 0);

        public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
            PagedResult<TOut>.Create(Items.Select(selector), Page, PageSize, TotalCount);
    }
}
