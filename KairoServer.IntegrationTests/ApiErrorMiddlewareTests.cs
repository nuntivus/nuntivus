using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KairoServer.Api.Common;
using KairoServer.Common;
using KairoServer.Common.Logging;
using Microsoft.AspNetCore.Http;

namespace KairoServer.IntegrationTests
{
    public sealed class ApiErrorMiddlewareTests
    {
        [Fact]
        public async Task Invoke_when_unhandled_exception_returns_sanitized_internal_problem_details()
        {
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new InvalidOperationException("database timeout with internal stack"),
                new NullLogger<ExceptionHandlingMiddleware>());

            var context = new DefaultHttpContext();
            context.TraceIdentifier = "req-internal";
            context.Request.Headers[ApiErrorResponse.CorrelationIdHeader] = "corr-internal";
            context.Response.Body = new MemoryStream();

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

            context.Response.Body.Position = 0;
            using var payload = await JsonDocument.ParseAsync(context.Response.Body);
            var root = payload.RootElement;

            Assert.Equal("unexpected", root.GetProperty("code").GetString());
            Assert.Equal(500, root.GetProperty("status").GetInt32());
            Assert.Equal("Internal Server Error.", root.GetProperty("title").GetString());
            Assert.Equal("An unexpected error occurred.", root.GetProperty("detail").GetString());
            Assert.Equal("req-internal", root.GetProperty("requestId").GetString());
            Assert.Equal("corr-internal", root.GetProperty("correlationId").GetString());
            Assert.DoesNotContain("database timeout", root.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Invoke_when_result_failure_exception_maps_to_problem_details_code_and_status()
        {
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new ResultFailureException(new[] { Error.NotFound("Customer was not found.") }),
                new NullLogger<ExceptionHandlingMiddleware>());

            var context = new DefaultHttpContext();
            context.TraceIdentifier = "req-not-found";
            context.Response.Body = new MemoryStream();

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

            context.Response.Body.Position = 0;
            using var payload = await JsonDocument.ParseAsync(context.Response.Body);
            var root = payload.RootElement;

            Assert.Equal("not_found", root.GetProperty("code").GetString());
            Assert.Equal(404, root.GetProperty("status").GetInt32());
            Assert.Equal("Resource not found.", root.GetProperty("title").GetString());
            Assert.Equal("Customer was not found.", root.GetProperty("detail").GetString());
            Assert.Equal("req-not-found", root.GetProperty("requestId").GetString());
            Assert.Equal("req-not-found", root.GetProperty("correlationId").GetString());
        }

        private sealed class NullLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }
}
