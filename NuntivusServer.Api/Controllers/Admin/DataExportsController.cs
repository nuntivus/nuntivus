using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Security;
using NuntivusServer.Application.Exports;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Auditing;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Operator)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("v1/exports")]
    public sealed class DataExportsController(IDataExportReader exports, IAuditLogRepository audits, IUnitOfWork uow,
        ILogger<DataExportsController> logger) : ControllerBase
    {
        [HttpGet("{entity}")]
        public async Task<IActionResult> Download(string entity, [FromQuery] DataExportRequest request, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
            var opened = await exports.OpenAsync(entity, request, cancellationToken);
            if (opened.IsFailure) return opened.ToOk(HttpContext);
            await using var session = opened.Value;
            var operation = Guid.NewGuid();
            await Audit("started", actor, operation, new { Entity = entity, Request = request, session.Count, session.Cutoff, Fields = session.Columns }, cancellationToken);
            Response.ContentType = request.Format == "csv" ? "text/csv; charset=utf-8" : "application/json; charset=utf-8";
            Response.Headers.ContentDisposition = $"attachment; filename=\"{entity}-{operation:N}.{request.Format}\"";
            Response.Headers["X-Export-Count"] = session.Count.ToString(CultureInfo.InvariantCulture);
            Response.Headers["X-Export-Cutoff"] = session.Cutoff.ToString("O", CultureInfo.InvariantCulture);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            long written = 0;
            var outcome = "failed";
            try
            {
                if (request.Format == "csv")
                {
                    await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(false), 16384, leaveOpen: true);
                    await writer.WriteLineAsync(string.Join(',', session.Columns.Select(DataExportFormatter.CsvCell)).AsMemory(), cancellationToken);
                    await foreach (var row in session.ReadAsync(cancellationToken))
                    {
                        await writer.WriteLineAsync(string.Join(',', session.Columns.Select(column => DataExportFormatter.CsvCell(row[column]))).AsMemory(), cancellationToken);
                        written++;
                        if (written % 100 == 0) await writer.FlushAsync(cancellationToken);
                    }
                    await writer.FlushAsync(cancellationToken);
                }
                else
                {
                    await using var writer = new Utf8JsonWriter(Response.Body);
                    writer.WriteStartObject(); writer.WriteString("cutoff", session.Cutoff); writer.WriteNumber("count", session.Count);
                    writer.WriteString("notice", "Metadata only; payloads, configuration, credentials and raw errors are excluded.");
                    writer.WriteStartArray("items");
                    await foreach (var row in session.ReadAsync(cancellationToken))
                    {
                        writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(row));
                        written++;
                        if (written % 100 == 0) await writer.FlushAsync(cancellationToken);
                    }
                    writer.WriteEndArray(); writer.WriteEndObject(); await writer.FlushAsync(cancellationToken);
                }
                outcome = "completed";
                return new EmptyResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
            finally
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await Audit(outcome, actor, operation, new { Entity = entity, Written = written, session.Count, session.Cutoff }, timeout.Token); }
                catch (Exception ex) { logger.LogError(ex, "Export {Operation} outcome audit could not be persisted.", operation); }
            }
        }
        private async Task Audit(string action, string actor, Guid operation, object details, CancellationToken cancellationToken)
        {
            try
            {
                uow.Begin(true);
                var now = DateTimeOffset.UtcNow;
                (await audits.AddAsync(AuditLogEntry.Create("export", action, actor, null, null, null, null, operation,
                    JsonSerializer.Serialize(details), now, now.AddDays(90)), uow, cancellationToken)).EnsureSuccess();
                uow.Commit();
            }
            catch { uow.Rollback(); throw; }
        }
    }
}
