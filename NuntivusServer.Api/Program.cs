using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Auditing;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Customers;
using NuntivusServer.Application.Dashboard;
using NuntivusServer.Application.Deliveries.Health;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Deliveries.Queries;
using NuntivusServer.Application.Deliveries.Recovery;
using NuntivusServer.Application.Events;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Application.Matching;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Security;
using NuntivusServer.Application.Settings;
using NuntivusServer.Application.Sources;
using NuntivusServer.Api.Common;
using NuntivusServer.Api.Common.OpenApi;
using NuntivusServer.Api.Security;
using NuntivusServer.Api.Controllers.Events;
using NuntivusServer.Api.Controllers.Sources;
using NuntivusServer.Common;
using NuntivusServer.Infastructure.Configuration;
using NuntivusServer.Infastructure.Events;
using NuntivusServer.Infastructure.Caching;
using NuntivusServer.Infastructure.Deliveries.Http;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Infastructure.Logging;
using NuntivusServer.Infastructure.Persistence.Caching;
using NuntivusServer.Infastructure.Persistence.Postgres;
using NuntivusServer.Infastructure.Persistence.Postgres.Migrations;
using NuntivusServer.Infastructure.Security;
using NuntivusServer.Domain.Deliveries;
using NuntivusServer.Domain.Sources;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddNuntivusEnvironmentVariableMappings();

builder.Services
    .AddOptions<HostOptions>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.HostOptions))
    .PostConfigure(options =>
    {
        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        }
    })
    .Validate(options => options.ShutdownTimeout > TimeSpan.Zero, "HostOptions:ShutdownTimeout must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Database))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
        {
            options.PostgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
        }
    })
    .Validate(NuntivusConfigurationValidation.HasPostgresConnection, "Configure Database:PostgresConnectionString (or ConnectionStrings:Postgres).")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseOption>>().Value);

builder.Services
    .AddOptions<DatabaseMigrationsOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DatabaseMigrations))
    .PostConfigure(options =>
    {
        if (options.Mode == DatabaseMigrationMode.Disabled && builder.Environment.IsProduction())
        {
            options.Mode = DatabaseMigrationMode.Validate;
        }
    })
    .Validate(options => !string.IsNullOrWhiteSpace(options.MigrationsPath), "DatabaseMigrations:MigrationsPath must not be empty.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseMigrationsOption>>().Value);

builder.Services
    .AddOptions<ApiSecurityOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.ApiSecurity))
    .Validate(options => options.MaxRequestBodyBytes > 0, "ApiSecurity:MaxRequestBodyBytes must be greater than zero.")
    .Validate(options => options.MaxEventPayloadBytes > 0, "ApiSecurity:MaxEventPayloadBytes must be greater than zero.")
    .Validate(options => options.MaxEventMetadataBytes > 0, "ApiSecurity:MaxEventMetadataBytes must be greater than zero.")
    .Validate(options => options.MaxEventPayloadBytes <= options.MaxRequestBodyBytes, "ApiSecurity:MaxEventPayloadBytes cannot exceed MaxRequestBodyBytes.")
    .Validate(options => options.MaxEventMetadataBytes <= options.MaxRequestBodyBytes, "ApiSecurity:MaxEventMetadataBytes cannot exceed MaxRequestBodyBytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ApiSecurityOption>>().Value);

builder.Services
    .AddOptions<LocalAuthOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.LocalAuth))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Issuer), "LocalAuth:Issuer is required when local auth is enabled.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Audience), "LocalAuth:Audience is required when local auth is enabled.")
    .Validate(options => !options.Enabled || options.AccessTokenLifetime > TimeSpan.Zero, "LocalAuth:AccessTokenLifetime must be greater than zero when local auth is enabled.")
    .Validate(options => !options.Enabled || options.ClockSkew >= TimeSpan.Zero, "LocalAuth:ClockSkew must be greater than or equal to zero when local auth is enabled.")
    .Validate(options => !options.Enabled || LocalAuthOption.HasValidSigningKey(options.SigningKey), "LocalAuth:SigningKey must be base64 and at least 32 bytes when local auth is enabled.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LocalAuthOption>>().Value);

builder.Services
    .AddOptions<CacheOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.Cache))
    .Validate(NuntivusConfigurationValidation.IsCacheTtlValid, "Cache:Ttl must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<CacheOption>>().Value);

builder.Services
    .AddOptions<DestinationCredentialOption>()
    .Bind(builder.Configuration.GetSection(NuntivusConfigurationSections.DestinationCredentials))
    .Validate(NuntivusConfigurationValidation.IsDestinationCredentialKeyValid, "DestinationCredentials:EncryptionKey must be base64 and decode to 32 bytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DestinationCredentialOption>>().Value);

builder.Services
    .AddOptions<HttpDestinationSecurityOption>()
    .Configure(options =>
    {
        var endpointSection = builder.Configuration.GetSection(NuntivusConfigurationSections.EndpointSecurity);
        if (endpointSection.Exists())
        {
            endpointSection.Bind(options);
        }
        else
        {
            builder.Configuration.GetSection(NuntivusConfigurationSections.LegacyDestinationSecurity).Bind(options);
        }

        NuntivusConfigurationValidation.ApplySafeEndpointDefaults(options, builder.Environment.IsProduction());
    })
    .Validate(NuntivusConfigurationValidation.IsEndpointSecurityValid, "EndpointSecurity:MaxResponseBodyBytes, MaxHeaders, and MaxConfigurationBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<HttpDestinationSecurityOption>>().Value);

builder.Services.AddSingleton<IConfigureOptions<KestrelServerOptions>>(sp =>
    new ConfigureNamedOptions<KestrelServerOptions>(Options.DefaultName, options =>
    {
        var apiSecurity = sp.GetRequiredService<ApiSecurityOption>();
        options.Limits.MaxRequestBodySize = apiSecurity.MaxRequestBodyBytes;
    }));

builder.AddNuntivusLogging();

builder.Services.AddControllers();

const string LocalDashboardCorsPolicy = "LocalDashboard";
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalDashboardCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin))
                {
                    return false;
                }

                return parsedOrigin.IsLoopback;
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("ETag");
    });
});

var authenticationAuthority = builder.Configuration["Authentication:Authority"];
var authenticationAudience = builder.Configuration["Authentication:Audience"];
var localAuth = builder.Configuration.GetSection(NuntivusConfigurationSections.LocalAuth).Get<LocalAuthOption>() ?? new LocalAuthOption();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        if (!string.IsNullOrWhiteSpace(authenticationAuthority))
        {
            options.Authority = authenticationAuthority;
        }

        if (!string.IsNullOrWhiteSpace(authenticationAudience))
        {
            options.Audience = authenticationAudience;
        }

        if (localAuth.Enabled)
        {
            var validIssuers = string.IsNullOrWhiteSpace(authenticationAuthority)
                ? new[] { localAuth.Issuer }
                : new[] { localAuth.Issuer, authenticationAuthority };

            var validAudiences = string.IsNullOrWhiteSpace(authenticationAudience)
                ? new[] { localAuth.Audience }
                : new[] { localAuth.Audience, authenticationAudience };

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudiences = validAudiences,
                ValidateLifetime = true,
                ClockSkew = localAuth.ClockSkew,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = localAuth.CreateSigningKey(),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role,
            };
        }

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                if (context.Response.HasStarted)
                    return;

                context.HandleResponse();

                var payload = ApiErrorResponse.From(
                    new[] { Error.Unauthorized("Authentication is required to access this resource.") },
                    context.HttpContext,
                    sanitizeUnexpected: false);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(payload, cancellationToken: context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                    return;

                var payload = ApiErrorResponse.From(
                    new[] { Error.Forbidden("You do not have permission to access this resource.") },
                    context.HttpContext,
                    sanitizeUnexpected: false);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(payload, cancellationToken: context.HttpContext.RequestAborted);
            },
        };
    });

builder.Services.AddPortal(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAuthorizationPolicies.Viewer, policy =>
        policy.RequireRole(
            AdminAuthorizationPolicies.ViewerRole,
            AdminAuthorizationPolicies.OperatorRole,
            AdminAuthorizationPolicies.AdminRole));

    options.AddPolicy(AdminAuthorizationPolicies.Operator, policy =>
        policy.RequireRole(
            AdminAuthorizationPolicies.OperatorRole,
            AdminAuthorizationPolicies.AdminRole));

    options.AddPolicy(AdminAuthorizationPolicies.Admin, policy =>
        policy.RequireRole(AdminAuthorizationPolicies.AdminRole));
});

builder.Services.AddSingleton(new RequiredTablesReadinessOption
{
    Schema = "public",
    Tables = RequiredTablesReadinessDefaults.CoreTables,
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresConnectionHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<PostgresRequiredTablesHealthCheck>("migration_tables", tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.OperationFilter<AuthorizeOperationSecurityFilter>();
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Bearer token. Example: 'Bearer eyJ...'.",
    });
});

builder.Services.AddSingleton<IDatabaseConnectionFactory>(
    sp => new PostgresDatabaseConnectionFactory(
        sp.GetRequiredService<DatabaseOption>().PostgresConnectionString!));
builder.Services.AddSingleton<IPostgresSchemaMigrationRunner, PostgresSchemaMigrationRunner>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheStore, InMemoryCacheStore>();

builder.Services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
builder.Services.AddScoped<ISourceRepository, PostgresSourceRepository>();
builder.Services.AddScoped<IDestinationRepository, PostgresDestinationRepository>();
builder.Services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
builder.Services.AddScoped<IAuditLogRepository, PostgresAuditLogRepository>();
builder.Services.AddScoped<IAdminUserRoleRepository, PostgresAdminUserRoleRepository>();
builder.Services.AddScoped<IAdminLocalCredentialRepository, PostgresAdminLocalCredentialRepository>();
builder.Services.AddScoped<ISettingsRepository, PostgresSettingsRepository>();
builder.Services.AddSingleton<IManagedSecretRepository, PostgresManagedSecretRepository>();

builder.Services.AddScoped<PostgresEventRepository>();
builder.Services.AddScoped<IEventRepository>(sp => new CachedEventRepository(
    sp.GetRequiredService<PostgresEventRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresCustomerRepository>();
builder.Services.AddScoped<ICustomerRepository>(sp => new CachedCustomerRepository(
    sp.GetRequiredService<PostgresCustomerRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<PostgresContractRepository>();
builder.Services.AddScoped<IContractRepository>(sp => new CachedContractRepository(
    sp.GetRequiredService<PostgresContractRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IDeliveryRepository, PostgresDeliveryRepository>();
builder.Services.AddSingleton<IDeliveryFactory, DeliveryFactory>();

builder.Services.AddScoped<PostgresEventDefinitionRepository>();
builder.Services.AddScoped<IEventDefinitionRepository>(sp => new CachedEventDefinitionRepository(
    sp.GetRequiredService<PostgresEventDefinitionRepository>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<CacheOption>()));

builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddSingleton<IEventSchemaValidator, JsonEventSchemaValidator>();
builder.Services.AddSingleton<ISchemaCompatibilityAnalyzer, SchemaCompatibilityAnalyzer>();
builder.Services.AddScoped<IEventSchemaEvolutionService, EventSchemaEvolutionService>();
builder.Services.AddScoped<IEventQueryService, EventQueryService>();
builder.Services.AddScoped<ISourceAdapter, HttpApiSourceAdapter>();
builder.Services.AddSingleton<ISourceConfigurationHandler, HttpSourceConfigurationHandler>();
foreach (var type in new[] { SourceType.RABBITMQ, SourceType.KAFKA, SourceType.SQS, SourceType.NATS,
    SourceType.HTTP_POLLING, SourceType.FOLDER, SourceType.S3 })
{
    builder.Services.AddSingleton<ISourceConfigurationHandler>(new PullSourceConfigurationHandler(type));
}
builder.Services.AddSingleton<ISourceConfigurationRegistry, SourceConfigurationRegistry>();
builder.Services.AddScoped<ISourceAdapterRegistry, SourceAdapterRegistry>();
builder.Services.AddScoped<ISourceIngestionService, SourceIngestionService>();
builder.Services.AddScoped<ISourceService, SourceService>();
builder.Services.AddScoped<IDestinationService, DestinationService>();
builder.Services.AddHttpClient("connection-test").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<ISourceConnectionTester, HttpApiSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, FolderSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, HttpPollingSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, RabbitMqSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, NatsSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, KafkaSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, SqsSourceConnectionTester>();
builder.Services.AddTransient<ISourceConnectionTester, S3SourceConnectionTester>();
builder.Services.AddTransient<IDestinationConnectionTester, HttpDestinationConnectionTester>();
builder.Services.AddTransient<HttpDeliveryExecutor>();
builder.Services.AddSingleton<NuntivusServer.Infastructure.Deliveries.Kafka.KafkaDeliveryExecutor>();
builder.Services.AddSingleton<NuntivusServer.Infastructure.Deliveries.Nats.NatsDeliveryExecutor>();
builder.Services.AddSingleton<NuntivusServer.Infastructure.Deliveries.RabbitMq.RabbitMqDeliveryExecutor>();
builder.Services.AddSingleton<NuntivusServer.Infastructure.Deliveries.Sqs.SqsDeliveryExecutor>();
builder.Services.AddHttpClient(HttpDeliveryExecutor.HttpClientName).ConfigurePrimaryHttpMessageHandler(sp =>
    new SocketsHttpHandler { AllowAutoRedirect = sp.GetRequiredService<HttpDestinationSecurityOption>().AllowRedirects });
builder.Services.AddTransient<IDestinationConnectionTester, KafkaDestinationConnectionTester>();
builder.Services.AddTransient<IDestinationConnectionTester, NatsDestinationConnectionTester>();
builder.Services.AddTransient<IDestinationConnectionTester, RabbitMqDestinationConnectionTester>();
builder.Services.AddTransient<IDestinationConnectionTester, SqsDestinationConnectionTester>();
builder.Services.AddScoped<IHttpSourceAuthenticator, HttpSourceAuthenticator>();
builder.Services.AddScoped<IEventDefinitionService, EventDefinitionService>();
builder.Services.AddScoped<IChangeImpactService, ChangeImpactService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IContractSloService, PostgresContractSloService>();
builder.Services.AddScoped<NuntivusServer.Application.Alerts.IAlertService, PostgresAlertService>();
builder.Services.AddScoped<NuntivusServer.Application.Alerts.IAlertOperationsService, PostgresAlertOperationsService>();
builder.Services.AddScoped<NuntivusServer.Application.Exports.IDataExportReader, PostgresDataExportReader>();
builder.Services.AddScoped<IContractTestEventService, ContractTestEventService>();
builder.Services.AddScoped<IContractDryRunService, ContractDryRunService>();
builder.Services.AddSingleton<NuntivusServer.Application.Deliveries.Execution.IDeliveryPayloadTransformer, NuntivusServer.Application.Deliveries.Execution.DeliveryPayloadTransformer>();
builder.Services.AddScoped<NuntivusServer.Application.Deliveries.Execution.IContractPayloadPreparer, NuntivusServer.Application.Deliveries.Execution.ContractPayloadPreparer>();
builder.Services.AddScoped<IDashboardOverviewQueryService, DashboardOverviewQueryService>();
builder.Services.AddSingleton<IContractFilterEvaluator, ContractFilterEvaluator>();
builder.Services.AddScoped<IDashboardTimeSeriesQueryService, PostgresDashboardTimeSeriesQueryService>();
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
builder.Services.AddScoped<ICustomerAdminSummaryQueryService, CustomerAdminSummaryQueryService>();
builder.Services.AddScoped<IContractAdminSummaryQueryService, ContractAdminSummaryQueryService>();
builder.Services.AddScoped<IAdminFailedDeliveryQueryService, AdminFailedDeliveryQueryService>();
builder.Services.AddScoped<IDeliveryQueryService, DeliveryQueryService>();
builder.Services.AddScoped<IDestinationHealthQueryService, DestinationHealthQueryService>();
builder.Services.AddScoped<IDeliveryRecoveryService, DeliveryRecoveryService>();
builder.Services.AddScoped<IInstanceSettingsService, InstanceSettingsService>();
builder.Services.AddScoped<IUserSettingsService, UserSettingsService>();
builder.Services.AddSingleton<IEventPublishRequestValidator, EventPublishRequestValidator>();
builder.Services.AddSingleton<IWebhookIngestRequestValidator, WebhookIngestRequestValidator>();

builder.Services.AddSingleton<IEndpointSecurityPolicy, EndpointSecurityPolicy>();
builder.Services.AddScoped<IAdminUserRoleService, AdminUserRoleService>();
builder.Services.AddScoped<IAdminLocalCredentialService, AdminLocalCredentialService>();
builder.Services.AddScoped<IAdminAuthenticationProvider, PostgresAdminAuthenticationProvider>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IAdminTokenIssuer, JwtAdminTokenIssuer>();

builder.Services.AddSingleton<IDestinationCredentialProtector, AesGcmDestinationCredentialProtector>();
builder.Services.AddSingleton<ISecretResolver, ProtectedSecretResolver>();

builder.Services.AddSingleton<IDestinationConfigHandler, HttpDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, KafkaDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, NatsDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, RabbitMqDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigHandler, SqsDestinationConfigHandler>();
builder.Services.AddSingleton<IDestinationConfigDispatcher, DestinationConfigDispatcher>();
builder.Services.AddSingleton<IDestinationConfigUpdateResolver, DestinationConfigUpdateResolver>();

var app = builder.Build();

var migrateOnly = args.Any(arg => string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase));
if (migrateOnly)
{
    await app.Services.EnsureDatabaseMigrationsAsync(DatabaseMigrationMode.Apply);
    return;
}

await app.Services.EnsureDatabaseMigrationsAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(LocalDashboardCorsPolicy);
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

public partial class Program { }
