using System;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using Microsoft.AspNetCore.Http;

namespace NuntivusServer.Api.Common
{
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAppLogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            IAppLogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.Warning("Request aborted by client (requestId {RequestId}).", context.TraceIdentifier);
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted)
                    throw;

                var response = MapException(ex, context);

                if ((response.Status ?? StatusCodes.Status500InternalServerError) >= StatusCodes.Status500InternalServerError)
                {
                    _logger.Error(ex, "Unhandled exception for {Method} {Path} (requestId {RequestId}).",
                        context.Request.Method,
                        context.Request.Path,
                        context.TraceIdentifier);
                }
                else
                {
                    _logger.Warning("Mapped exception to HTTP {Status} for {Method} {Path} (requestId {RequestId}).",
                        response.Status,
                        context.Request.Method,
                        context.Request.Path,
                        context.TraceIdentifier);
                }

                context.Response.Clear();
                context.Response.StatusCode = response.Status ?? StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";

                await context.Response.WriteAsJsonAsync(response, cancellationToken: context.RequestAborted);
            }
        }

        private static ApiErrorResponse MapException(Exception ex, HttpContext context)
        {
            return ex switch
            {
                ResultFailureException failure => ApiErrorResponse.From(failure.Errors, context),
                BadHttpRequestException => ApiErrorResponse.From(
                    new[] { Error.Validation("The request payload is invalid.") },
                    context,
                    sanitizeUnexpected: false),
                ArgumentException argument => ApiErrorResponse.From(
                    new[] { Error.Validation(argument.Message) },
                    context,
                    sanitizeUnexpected: false),
                _ => ApiErrorResponse.From(
                    new[] { Error.Unexpected("An unexpected error occurred.") },
                    context),
            };
        }
    }
}
