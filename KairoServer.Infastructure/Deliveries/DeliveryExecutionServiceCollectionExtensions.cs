using KairoServer.Application.Deliveries.Execution;
using KairoServer.Infastructure.Deliveries.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KairoServer.Infastructure.Deliveries
{
    /// <summary>
    /// DI wiring for the Delivery execution boundary. Registers the HTTP
    /// transport implementation, the dispatcher, and supporting services.
    /// Additional destinations register their own <see cref="IDeliveryExecutor"/>
    /// here (or via their own extension) without touching Delivery Core.
    /// </summary>
    public static class DeliveryExecutionServiceCollectionExtensions
    {
        public static IServiceCollection AddDeliveryExecution(this IServiceCollection services)
        {
            services.AddHttpClient(HttpDeliveryExecutor.HttpClientName);

            services.AddSingleton<ISecretResolver, NullSecretResolver>();
            services.AddSingleton<IDeliveryExecutor, HttpDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutorDispatcher, DeliveryExecutorDispatcher>();

            return services;
        }
    }
}
