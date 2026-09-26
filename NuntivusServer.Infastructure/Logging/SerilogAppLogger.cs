using NuntivusServer.Common.Logging;
using Serilog;
using Serilog.Events;
using System;

namespace NuntivusServer.Infastructure.Logging
{
    /// <summary>
    /// Serilog-backed implementation of <see cref="IAppLogger"/>.
    /// </summary>
    internal class SerilogAppLogger : IAppLogger
    {
        private readonly ILogger _logger;

        public SerilogAppLogger(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Debug(string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Debug, messageTemplate, args);

        public void Information(string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Information, messageTemplate, args);

        public void Warning(string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Warning, messageTemplate, args);

        public void Error(string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Error, messageTemplate, args);

        public void Error(Exception exception, string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Error, exception, messageTemplate, args);

        public void Fatal(string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Fatal, messageTemplate, args);

        public void Fatal(Exception exception, string messageTemplate, params object?[] args)
            => _logger.Write(LogEventLevel.Fatal, exception, messageTemplate, args);
    }

    /// <summary>
    /// Category-typed Serilog logger. Uses <see cref="ILogger.ForContext{T}()"/>
    /// so the emitted <c>SourceContext</c> matches <typeparamref name="T"/>.
    /// </summary>
    internal sealed class SerilogAppLogger<T> : SerilogAppLogger, IAppLogger<T>
    {
        public SerilogAppLogger(ILogger logger)
            : base((logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<T>())
        {
        }
    }
}
