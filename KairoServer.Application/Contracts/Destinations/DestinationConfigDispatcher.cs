using System;
using System.Collections.Generic;
using System.Linq;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Dispatches a <see cref="DestinationConfig"/> to the registered
    /// <see cref="IDestinationConfigHandler"/> matching its runtime type.
    /// </summary>
    public sealed class DestinationConfigDispatcher : IDestinationConfigDispatcher
    {
        private readonly IReadOnlyDictionary<Type, IDestinationConfigHandler> _handlers;
        private readonly IReadOnlyDictionary<DomainDestinationType, IDestinationConfigHandler> _handlersByDestinationType;

        public DestinationConfigDispatcher(IEnumerable<IDestinationConfigHandler> handlers)
        {
            if (handlers is null) throw new ArgumentNullException(nameof(handlers));

            var materialized = handlers.ToArray();
            _handlers = materialized.ToDictionary(h => h.ConfigType);
            _handlersByDestinationType = materialized.ToDictionary(h => h.DestinationType);
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

        public Result<string> Redact(Destination destination)
        {
            if (destination is null)
                return Result.Failure<string>(Error.Validation("Destination is required."));

            if (!_handlersByDestinationType.TryGetValue(destination.Type, out var handler))
            {
                return Result.Failure<string>(Error.Validation(
                    $"Unsupported destination type '{destination.Type}'."));
            }

            return handler.Redact(destination.Configuration);
        }

        public Result<Destination> Rotate(Destination current, Destination next)
        {
            if (current is null)
                return Result.Failure<Destination>(Error.Validation("Current destination is required."));
            if (next is null)
                return Result.Failure<Destination>(Error.Validation("Next destination is required."));

            if (!_handlersByDestinationType.TryGetValue(next.Type, out var handler))
                return Result.Success(next);

            return handler.Rotate(current, next);
        }
    }
}
