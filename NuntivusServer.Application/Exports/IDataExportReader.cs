using System.Collections.Generic;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Exports
{
    public sealed class DataExportRequest
    {
        public string Format { get; set; } = "json";
        public Guid? Id { get; set; }
        public Guid? EventId { get; set; }
        public Guid? ContractId { get; set; }
        public string? CustomerId { get; set; }
        public string? Type { get; set; }
        public string? Source { get; set; }
        public string? EventType { get; set; }
        public string? Status { get; set; }
        public bool FailuresOnly { get; set; }
        public string? DestinationType { get; set; }
        public string? FailureCategory { get; set; }
        public string? Search { get; set; }
        public bool? Enabled { get; set; }
        public DateTimeOffset? CreatedFrom { get; set; }
        public DateTimeOffset? CreatedTo { get; set; }
        public DateTimeOffset? OccurredFrom { get; set; }
        public DateTimeOffset? OccurredTo { get; set; }
        public DateTimeOffset? ReceivedFrom { get; set; }
        public DateTimeOffset? ReceivedTo { get; set; }
    }
    public interface IDataExportSession : IAsyncDisposable
    {
        long Count { get; }
        DateTimeOffset Cutoff { get; }
        IReadOnlyList<string> Columns { get; }
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync(CancellationToken cancellationToken);
    }
    public interface IDataExportReader
    {
        Task<Result<IDataExportSession>> OpenAsync(string entity, DataExportRequest request, CancellationToken cancellationToken);
    }
}
