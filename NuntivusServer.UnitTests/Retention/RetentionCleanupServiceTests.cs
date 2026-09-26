using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Retention;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;

namespace NuntivusServer.UnitTests.Retention
{
    public sealed class RetentionCleanupServiceTests
    {
        [Fact]
        public async Task RunCleanupCycle_aggregates_batched_deletes_and_stops_on_limits()
        {
            var repo = new FakeRetentionCleanupRepository(
                processedOutbox: new Queue<int>(new[] { 2, 1 }),
                deliveryAttempts: new Queue<int>(new[] { 0 }),
                deliveries: new Queue<int>(new[] { 2, 2, 2, 2 }),
                events: new Queue<int>(new[] { 1 }),
                auditLogs: new Queue<int>(new[] { 0 }));

            var options = new RetentionCleanupOption
            {
                Enabled = true,
                BatchSize = 2,
                MaxBatchesPerCycle = 3,
                InterBatchDelay = TimeSpan.Zero,
                EventRetention = TimeSpan.FromDays(30),
                DeliveryRetention = TimeSpan.FromDays(14),
                DeliveryAttemptRetention = TimeSpan.FromDays(14),
                ProcessedOutboxRetention = TimeSpan.FromDays(7),
            };

            var sut = new RetentionCleanupService(repo, options, new NullLogger<RetentionCleanupService>());

            var result = await sut.RunCleanupCycleAsync(CancellationToken.None);

            result.EnsureSuccess();
            Assert.Equal(3, result.Value.DeletedProcessedOutbox);
            Assert.Equal(0, result.Value.DeletedDeliveryAttempts);
            Assert.Equal(6, result.Value.DeletedDeliveries);
            Assert.Equal(1, result.Value.DeletedEvents);
            Assert.Equal(0, result.Value.DeletedAuditLogs);
            Assert.Equal(10, result.Value.TotalDeletedRows);
            Assert.Equal(8, result.Value.BatchesProcessed);
        }

        [Fact]
        public async Task RunCleanupCycle_fails_when_batch_size_is_invalid()
        {
            var sut = new RetentionCleanupService(
                new FakeRetentionCleanupRepository(),
                new RetentionCleanupOption
                {
                    Enabled = true,
                    BatchSize = 0,
                    MaxBatchesPerCycle = 1,
                },
                new NullLogger<RetentionCleanupService>());

            var result = await sut.RunCleanupCycleAsync(CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
        }

        private sealed class FakeRetentionCleanupRepository : IRetentionCleanupRepository
        {
            private readonly Queue<int> _processedOutbox;
            private readonly Queue<int> _deliveryAttempts;
            private readonly Queue<int> _deliveries;
            private readonly Queue<int> _events;
            private readonly Queue<int> _auditLogs;

            public FakeRetentionCleanupRepository(
                Queue<int>? processedOutbox = null,
                Queue<int>? deliveryAttempts = null,
                Queue<int>? deliveries = null,
                Queue<int>? events = null,
                Queue<int>? auditLogs = null)
            {
                _processedOutbox = processedOutbox ?? new Queue<int>(new[] { 0 });
                _deliveryAttempts = deliveryAttempts ?? new Queue<int>(new[] { 0 });
                _deliveries = deliveries ?? new Queue<int>(new[] { 0 });
                _events = events ?? new Queue<int>(new[] { 0 });
                _auditLogs = auditLogs ?? new Queue<int>(new[] { 0 });
            }

            public Task<Result<int>> DeleteProcessedOutboxBatchAsync(DateTimeOffset processedBefore, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Dequeue(_processedOutbox)));

            public Task<Result<int>> DeleteDeliveryAttemptsBatchAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Dequeue(_deliveryAttempts)));

            public Task<Result<int>> DeleteDeliveriesBatchAsync(DateTimeOffset updatedBefore, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Dequeue(_deliveries)));

            public Task<Result<int>> DeleteEventsBatchAsync(DateTimeOffset receivedBefore, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Dequeue(_events)));

            public Task<Result<int>> DeleteAuditLogsBatchAsync(DateTimeOffset createdBefore, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Dequeue(_auditLogs)));

            private static int Dequeue(Queue<int> queue) => queue.Count > 0 ? queue.Dequeue() : 0;
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
