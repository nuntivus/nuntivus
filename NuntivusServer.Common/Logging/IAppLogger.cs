using System;

namespace NuntivusServer.Common.Logging
{
    /// <summary>
    /// Application-wide logging abstraction. Kept intentionally minimal so
    /// concrete providers (Serilog, Microsoft.Extensions.Logging, etc.) can
    /// be swapped without touching call sites.
    /// </summary>
    public interface IAppLogger
    {
        void Debug(string messageTemplate, params object?[] args);
        void Information(string messageTemplate, params object?[] args);
        void Warning(string messageTemplate, params object?[] args);
        void Error(string messageTemplate, params object?[] args);
        void Error(Exception exception, string messageTemplate, params object?[] args);
        void Fatal(string messageTemplate, params object?[] args);
        void Fatal(Exception exception, string messageTemplate, params object?[] args);
    }

    /// <summary>
    /// Category-typed variant, mirroring <c>ILogger&lt;T&gt;</c> conventions
    /// so DI can inject a logger scoped to the consuming type.
    /// </summary>
    public interface IAppLogger<T> : IAppLogger
    {
    }
}
