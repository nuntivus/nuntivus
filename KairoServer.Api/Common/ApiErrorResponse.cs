using System.Collections.Generic;
using System.Linq;
using KairoServer.Common;

namespace KairoServer.Api.Common
{
    /// <summary>
    /// Standard error body returned by controllers when a Result is a failure.
    /// </summary>
    public sealed record ApiErrorResponse(
        string Code,
        string Message,
        IReadOnlyList<ApiErrorDetail> Errors)
    {
        public static ApiErrorResponse From(IReadOnlyList<Error> errors)
        {
            var primary = errors.Count > 0 ? errors[0] : new Error("unexpected", "The operation failed.");
            return new ApiErrorResponse(
                primary.Code,
                primary.Message,
                errors.Select(e => new ApiErrorDetail(e.Code, e.Message)).ToArray());
        }
    }

    public sealed record ApiErrorDetail(string Code, string Message);
}
