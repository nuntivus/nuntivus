using Microsoft.Extensions.DependencyInjection;
using NuntivusServer.Application.Alerts;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.Alerts;
using NuntivusServer.Worker.Alerts;

namespace NuntivusServer.IntegrationTests
{
    public sealed class AlertWorkerTests
    {
        [Fact]
        public async Task Evaluation_runs_while_webhook_sender_is_blocked()
        {
            var alerts = new FakeAlerts();
            var sender = new BlockingSender();
            using var provider = new ServiceCollection().AddSingleton<IAlertService>(alerts).BuildServiceProvider();
            using var webhooks = new AlertWebhookWorker(new Queue(), sender, new NullLogger<AlertWebhookWorker>());
            using var evaluator = new AlertWorker(provider.GetRequiredService<IServiceScopeFactory>(), new NullLogger<AlertWorker>());
            try
            {
                await webhooks.StartAsync(CancellationToken.None);
                await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await evaluator.StartAsync(CancellationToken.None);
                await alerts.Evaluated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(sender.Finished);
            }
            finally
            {
                await evaluator.StopAsync(CancellationToken.None);
                await webhooks.StopAsync(CancellationToken.None);
            }
        }

        private sealed class BlockingSender : IAlertNotificationSender
        {
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Finished { get; private set; }
            public async Task<Result> SendAsync(AlertWebhookMessage message, CancellationToken cancellationToken)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                Finished = true;
                return Result.Success();
            }
        }
        private sealed class Queue : IAlertWebhookQueue
        {
            public Task<Result<AlertWebhookMessage?>> ClaimAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success<AlertWebhookMessage?>(
                new AlertWebhookMessage(Guid.NewGuid(), Guid.NewGuid(), "https://example.com", null, "{}", 1)));
            public Task<Result> CompleteAsync(AlertWebhookMessage message, bool delivered, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        }
        private sealed class FakeAlerts : IAlertService
        {
            public TaskCompletionSource Evaluated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<Result<int>> EvaluateAsync(CancellationToken cancellationToken) { Evaluated.TrySetResult(); return Task.FromResult(Result.Success(0)); }
            public Task<Result<AlertRuleView>> SaveRuleAsync(Guid id, AlertRuleRequest request, string actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result<PagedResult<AlertRuleView>>> ListRulesAsync(int page, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result<PagedResult<AlertNotificationView>>> ListNotificationsAsync(int page, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<Result<AlertNotificationView>> AcknowledgeAsync(Guid id, string actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
