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
            Func<T, IActionResult> onSuccess)
        {
            return result.IsSuccess
                ? onSuccess(result.Value)
                : ToErrorResult(result.Errors);
        }

        public static IActionResult ToOk<T>(this Result<T> result) =>
            result.ToActionResult(v => new OkObjectResult(v));

        public static IActionResult ToPagedOk<TDomain, TDto>(
            this Result<PagedResult<TDomain>> result,
            Func<TDomain, TDto> selector)
        {
            return result.ToActionResult(p =>
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

        public static IActionResult ToPagedOk<T>(this Result<PagedResult<T>> result) =>
            result.ToPagedOk(x => x);

        private static IActionResult ToErrorResult(
            System.Collections.Generic.IReadOnlyList<Error> errors)
        {
            var status = MapStatus(errors.Count > 0 ? errors[0].Code : "unexpected");
            return new ObjectResult(ApiErrorResponse.From(errors))
            {
                StatusCode = status
            };
        }

        private static int MapStatus(string code) => code switch
        {
            "validation" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "forbidden" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
