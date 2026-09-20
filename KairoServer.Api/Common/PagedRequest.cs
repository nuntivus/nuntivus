namespace KairoServer.Api.Common
{
    /// <summary>
    /// Shared query-string binding model for endpoints that return a paged list.
    /// Bind with <c>[FromQuery]</c> so <c>?page=</c> and <c>?pageSize=</c> are
    /// mapped by the default MVC model binder. Value bounds are enforced by
    /// the application layer (see e.g. <c>CustomerService.MaxPageSize</c>).
    /// </summary>
    public class PagedRequest
    {
        public const int DefaultPage = 1;
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        public int Page { get; set; } = DefaultPage;

        public int PageSize { get; set; } = DefaultPageSize;
    }
}
