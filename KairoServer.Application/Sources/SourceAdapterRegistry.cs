using System;
using System.Collections.Generic;
using KairoServer.Common;
using KairoServer.Domain.Sources;

namespace KairoServer.Application.Sources
{
    public sealed class SourceAdapterRegistry : ISourceAdapterRegistry
    {
        private readonly Dictionary<SourceType, ISourceAdapter> _adapters;

        public SourceAdapterRegistry(IEnumerable<ISourceAdapter> adapters)
        {
            _adapters = new Dictionary<SourceType, ISourceAdapter>();

            foreach (var adapter in adapters)
            {
                if (!_adapters.TryAdd(adapter.Type, adapter))
                {
                    throw new InvalidOperationException($"Duplicate source adapter registration for type '{adapter.Type}'.");
                }
            }
        }

        public Result<ISourceAdapter> Get(SourceType sourceType)
        {
            return _adapters.TryGetValue(sourceType, out var adapter)
                ? Result.Success(adapter)
                : Result.Failure<ISourceAdapter>(Error.NotFound($"No source adapter is registered for type '{sourceType}'."));
        }
    }
}
