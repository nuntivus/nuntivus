using NuntivusServer.Application.Persistence;
using NuntivusServer.Common.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace NuntivusServer.Worker.Sources
{
    public sealed class SourceSupervisorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ISourceIngestionStateRepository _state;
        private readonly IAppLogger<SourceSupervisorWorker> _logger;
        private readonly string _owner = $"source-{Environment.MachineName}-{Guid.NewGuid():N}";
        private readonly Dictionary<string, RunningSource> _running = new();
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

        public SourceSupervisorWorker(IServiceScopeFactory scopes, ISourceIngestionStateRepository state,
            IAppLogger<SourceSupervisorWorker> logger)
        {
            _scopes = scopes;
            _state = state;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try { await ReconcileAsync(stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.Error(ex, "Source supervisor reconciliation failed."); }
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            finally
            {
                foreach (var running in _running.Values) running.Stop.Cancel();
                try { await Task.WhenAll(_running.Values.Select(running => running.Task)); }
                catch (Exception ex) { _logger.Error(ex, "Source supervisor shutdown failed."); }
                foreach (var running in _running.Values) running.Stop.Dispose();
                _running.Clear();
            }
        }

        private async Task ReconcileAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopes.CreateScope();
            var sources = (await scope.ServiceProvider.GetRequiredService<ISourceRepository>()
                .ListEnabledAsync(cancellationToken)).EnsureValue();
            var availableTypes = scope.ServiceProvider.GetServices<ISourceRunner>().Select(runner => runner.Type).ToHashSet();
            var active = sources.Where(source => availableTypes.Contains(source.Type))
                .ToDictionary(source => source.Id.Value);

            foreach (var (id, running) in _running.ToArray())
            {
                if (running.Task.IsCompleted || !active.TryGetValue(id, out var source)
                    || source.UpdatedAt != running.UpdatedAt)
                {
                    running.Stop.Cancel();
                    await running.Task;
                    running.Stop.Dispose();
                    _running.Remove(id);
                    continue;
                }
                var renewed = await _state.RenewAsync(running.Lease, LeaseDuration, cancellationToken);
                if (renewed.IsFailure || !renewed.Value)
                {
                    running.Stop.Cancel();
                    await running.Task;
                    running.Stop.Dispose();
                    _running.Remove(id);
                }
            }

            foreach (var (id, source) in active)
            {
                if (_running.ContainsKey(id)) continue;
                var leaseResult = await _state.TryAcquireAsync(id, _owner, LeaseDuration, cancellationToken);
                if (leaseResult.IsFailure)
                {
                    _logger.Warning("Unable to acquire Source lease (SourceId {SourceId}).", id);
                    continue;
                }
                if (leaseResult.Value is null) continue;
                var lease = leaseResult.Value;
                var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var task = RunSourceAsync(source, lease, stop.Token);
                _running.Add(id, new RunningSource(lease, source.UpdatedAt, stop, task));
            }
        }

        private async Task RunSourceAsync(NuntivusServer.Domain.Sources.Source source, SourceLease lease, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var runner = scope.ServiceProvider.GetServices<ISourceRunner>().Single(item => item.Type == source.Type);
                await runner.RunAsync(source, lease, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { _logger.Error(ex, "Source runner failed (SourceId {SourceId}).", source.Id.Value); }
            finally
            {
                var release = await _state.ReleaseAsync(lease, CancellationToken.None);
                if (release.IsFailure) _logger.Warning("Unable to release Source lease (SourceId {SourceId}).", source.Id.Value);
            }
        }

        private sealed record RunningSource(SourceLease Lease, DateTimeOffset UpdatedAt, CancellationTokenSource Stop, Task Task);
    }
}
