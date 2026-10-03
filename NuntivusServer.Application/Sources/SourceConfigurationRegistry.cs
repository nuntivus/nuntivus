using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class SourceConfigurationRegistry : ISourceConfigurationRegistry
    {
        private readonly IReadOnlyDictionary<SourceType, ISourceConfigurationHandler> _handlers;

        public SourceConfigurationRegistry(IEnumerable<ISourceConfigurationHandler> handlers)
        {
            _handlers = handlers.ToDictionary(handler => handler.Type);
        }

        public bool IsOperational(SourceType type) => _handlers.ContainsKey(type);

        public Result<string> Normalize(SourceType type, string configuration) =>
            _handlers.TryGetValue(type, out var handler)
                ? handler.Normalize(configuration)
                : Result.Failure<string>(Error.Validation($"Source type '{type}' is not operational."));
    }
}
