using System;
using NuntivusServer.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NuntivusServer.Api.Common
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
            if (payload.Status >= StatusCodes.Status500InternalServerError)
            {
                var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(ResultActionResultExtensions));
                foreach (var error in errors)
                {
                    logger.LogError(error.Exception,
                        "Request failed for {Method} {Path} (requestId {RequestId}, correlationId {CorrelationId}): {ErrorCode}",
                        httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier,
                        httpContext.Request.Headers[ApiErrorResponse.CorrelationIdHeader].ToString(), error.Code);
                }
            }
            return new ObjectResult(payload)
            {
                StatusCode = payload.Status
            };
        }
    }
}
