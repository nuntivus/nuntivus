using KairoServer.Application.Deliveries.Execution;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Security;
using KairoServer.Infastructure.Deliveries.Kafka;
using KairoServer.Infastructure.Deliveries.Nats;
using KairoServer.Infastructure.Deliveries.RabbitMq;
using KairoServer.Infastructure.Deliveries.Sqs;
using KairoServer.Infastructure.Deliveries.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http;

namespace KairoServer.Infastructure.Deliveries
{
    /// <summary>
    /// DI wiring for the Delivery execution boundary. Registers the HTTP
    /// transport implementations, the dispatcher, and supporting services.
    /// Additional destinations register their own <see cref="IDeliveryExecutor"/>
    /// here (or via their own extension) without touching Delivery Core.
    /// </summary>
    public static class DeliveryExecutionServiceCollectionExtensions
    {
        public static IServiceCollection AddDeliveryExecution(this IServiceCollection services)
        {
            services.TryAddSingleton(new HttpDestinationSecurityOption());
            services.TryAddSingleton<IEndpointSecurityPolicy, EndpointSecurityPolicy>();

            services.AddHttpClient(HttpDeliveryExecutor.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var security = sp.GetRequiredService<HttpDestinationSecurityOption>();
                    return new SocketsHttpHandler
                    {
                        AllowAutoRedirect = security.AllowRedirects,
                        AutomaticDecompression = DecompressionMethods.None
                    };
                });

            services.TryAddSingleton(new DestinationCredentialOption());
            services.TryAddSingleton<IDestinationCredentialProtector, AesGcmDestinationCredentialProtector>();
            services.AddSingleton<ISecretResolver, ProtectedSecretResolver>();
            services.AddSingleton<IDeliveryExecutor, HttpDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutor, KafkaDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutor, NatsDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutor, RabbitMqDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutor, SqsDeliveryExecutor>();
            services.AddSingleton<IDeliveryExecutorDispatcher, DeliveryExecutorDispatcher>();
            services.AddSingleton<IDeliveryPayloadTransformer, DeliveryPayloadTransformer>();

            return services;
        }
    }
}
