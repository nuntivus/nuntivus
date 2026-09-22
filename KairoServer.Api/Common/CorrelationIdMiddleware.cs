using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace KairoServer.Api.Common
{
    public sealed class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var incoming = context.Request.Headers[ApiErrorResponse.CorrelationIdHeader].ToString();
            var correlationId = string.IsNullOrWhiteSpace(incoming)
                ? context.TraceIdentifier
                : incoming.Trim();

            context.TraceIdentifier = correlationId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[ApiErrorResponse.CorrelationIdHeader] = correlationId;
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
