using System;
using System.Collections.Generic;
using System.Linq;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Dispatches a <see cref="DestinationConfig"/> to the registered
    /// <see cref="IDestinationConfigHandler"/> matching its runtime type.
    /// </summary>
    public sealed class DestinationConfigDispatcher : IDestinationConfigDispatcher
    {
        private readonly IReadOnlyDictionary<Type, IDestinationConfigHandler> _handlers;

        public DestinationConfigDispatcher(IEnumerable<IDestinationConfigHandler> handlers)
        {
            if (handlers is null) throw new ArgumentNullException(nameof(handlers));
            _handlers = handlers.ToDictionary(h => h.ConfigType);
        }

        public Result<Destination> Build(DestinationConfig? configuration)
        {
            if (configuration is null)
            {
                return Result.Failure<Destination>(Error.Validation(
                    "Destination configuration is required."));
            }

            if (!_handlers.TryGetValue(configuration.GetType(), out var handler))
            {
                return Result.Failure<Destination>(Error.Validation(
                    $"Unsupported destination configuration '{configuration.GetType().Name}'."));
            }

            return handler.Build(configuration);
        }
    }
}
