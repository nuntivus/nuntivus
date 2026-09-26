using System;
using System.Collections.Generic;
using System.Linq;
using NuntivusServer.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Common
{
    public sealed class ApiErrorResponse : ProblemDetails
    {
        public const string CorrelationIdHeader = "X-Correlation-Id";

        public string Code { get; init; } = "unexpected";

        public string Message => Detail ?? string.Empty;

        public IReadOnlyList<ApiErrorDetail> Errors { get; init; } = [];

        public static ApiErrorResponse From(
            IReadOnlyList<Error> errors,
            HttpContext? httpContext = null,
            bool sanitizeUnexpected = true)
        {
            var primary = errors.Count > 0 ? errors[0] : new Error("unexpected", "The operation failed.");

            var normalizedCode = NormalizeCode(primary.Code);
            var descriptor = Describe(normalizedCode);

            var response = new ApiErrorResponse
            {
                Type = descriptor.Type,
                Title = descriptor.Title,
                Status = descriptor.Status,
                Detail = normalizeDetail(primary, normalizedCode, sanitizeUnexpected),
                Instance = httpContext?.Request.Path.Value,
                Code = normalizedCode,
                Errors = errors.Select(e =>
                {
                    var code = NormalizeCode(e.Code);
                    var detail = normalizeDetail(e, code, sanitizeUnexpected);
                    return new ApiErrorDetail(code, detail);
                }).ToArray(),
            };

            AttachRequestMetadata(response, httpContext);
            return response;

            static string normalizeDetail(Error error, string code, bool sanitize)
            {
                if (sanitize && code == "unexpected")
                    return "An unexpected error occurred.";

                return string.IsNullOrWhiteSpace(error.Message)
                    ? "The operation failed."
                    : error.Message;
            }
        }

        public static int MapStatusCode(string? code)
        {
            return Describe(NormalizeCode(code)).Status;
        }

        private static string NormalizeCode(string? code)
        {
            return string.IsNullOrWhiteSpace(code)
                ? "unexpected"
                : code.Trim().ToLowerInvariant();
        }

        private static (int Status, string Title, string Type) Describe(string code)
        {
            return code switch
            {
                "validation" => (StatusCodes.Status400BadRequest, "Validation failed.", "https://httpstatuses.com/400"),
                "not_found" => (StatusCodes.Status404NotFound, "Resource not found.", "https://httpstatuses.com/404"),
                "conflict" => (StatusCodes.Status409Conflict, "Conflict.", "https://httpstatuses.com/409"),
                "unauthorized" => (StatusCodes.Status401Unauthorized, "Unauthorized.", "https://httpstatuses.com/401"),
                "forbidden" => (StatusCodes.Status403Forbidden, "Forbidden.", "https://httpstatuses.com/403"),
                _ => (StatusCodes.Status500InternalServerError, "Internal Server Error.", "https://httpstatuses.com/500"),
            };
        }

        private static void AttachRequestMetadata(ApiErrorResponse response, HttpContext? httpContext)
        {
            if (httpContext is null)
                return;

            var requestId = httpContext.TraceIdentifier;
            var correlationId = httpContext.Request.Headers.TryGetValue(CorrelationIdHeader, out var values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(correlationId))
                correlationId = requestId;

            if (!string.IsNullOrWhiteSpace(requestId))
                response.Extensions["requestId"] = requestId;

            if (!string.IsNullOrWhiteSpace(correlationId))
                response.Extensions["correlationId"] = correlationId;
        }
    }

    public sealed record ApiErrorDetail(string Code, string Message);
}
