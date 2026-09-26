using NuntivusServer.Common.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace NuntivusServer.Infastructure.Logging
{
    /// <summary>
    /// DI helpers to plug Serilog in as the concrete <see cref="IAppLogger"/> provider.
    /// </summary>
    public static class SerilogLoggingExtensions
    {
        /// <summary>
        /// Configures Serilog from <c>appsettings.json</c> (falling back to a sensible
        /// console default) and registers <see cref="IAppLogger"/> / <see cref="IAppLogger{T}"/>.
        /// </summary>
        public static IHostApplicationBuilder AddNuntivusLogging(this IHostApplicationBuilder builder)
        {
            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console();

            Log.Logger = loggerConfig.CreateLogger();

            builder.Services.AddSerilog(Log.Logger, dispose: true);

            builder.Services.AddSingleton<ILogger>(Log.Logger);
            builder.Services.AddSingleton<IAppLogger>(sp => new SerilogAppLogger(Log.Logger));
            builder.Services.AddSingleton(typeof(IAppLogger<>), typeof(SerilogAppLogger<>));

            return builder;
        }
    }
}
