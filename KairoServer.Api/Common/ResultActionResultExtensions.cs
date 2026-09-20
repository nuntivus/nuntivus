using System;
using KairoServer.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Common
{
    /// <summary>
    /// Maps <see cref="Result{T}"/> and <see cref="PagedResult{T}"/> to consistent
    /// HTTP responses across controllers.
    /// </summary>
    public static class ResultActionResultExtensions
    {
        public static IActionResult ToActionResult<T>(
            this Result<T> result,
            HttpContext httpContext,
            Func<T, IActionResult> onSuccess)
        {
            return result.IsSuccess
                ? onSuccess(result.Value)
                : ToErrorResult(result.Errors, httpContext);
        }

        public static IActionResult ToOk<T>(
            this Result<T> result,
            HttpContext httpContext) =>
            result.ToActionResult(httpContext, v => new OkObjectResult(v));

        public static IActionResult ToPagedOk<TDomain, TDto>(
            this Result<PagedResult<TDomain>> result,
            HttpContext httpContext,
            Func<TDomain, TDto> selector)
        {
            return result.ToActionResult(httpContext, p =>
            {
                var mapped = p.Map(selector);
                var body = new PagedResponse<TDto>(
                    mapped.Items,
                    mapped.Page,
                    mapped.PageSize,
                    mapped.TotalCount,
                    mapped.TotalPages,
                    mapped.HasPrevious,
                    mapped.HasNext);
                return new OkObjectResult(body);
            });
        }

        public static IActionResult ToPagedOk<T>(
            this Result<PagedResult<T>> result,
            HttpContext httpContext) =>
            result.ToPagedOk(httpContext, x => x);

        private static IActionResult ToErrorResult(
            System.Collections.Generic.IReadOnlyList<Error> errors,
            HttpContext httpContext)
        {
            var payload = ApiErrorResponse.From(errors, httpContext);
            return new ObjectResult(payload)
            {
                StatusCode = payload.Status
            };
        }
    }
}
