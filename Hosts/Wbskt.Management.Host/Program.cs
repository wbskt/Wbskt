using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Devices;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Extensions;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Hosting;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Services.Readings;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Primitives;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Extensions;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Management.Host.Telemetry;

namespace Wbskt.Management.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "connectionstrings.json", "rabbitmq.json");
        builder.AddWbsktTelemetry(ManagementMetrics.MeterName, QueuedEventBus.MeterName);

        // A Devices instance serves only device registration and login (see HostRole). It keeps the
        // same service registrations, minus the console's background work and live feed.
        var role = HostRoles.FromConfiguration(builder.Configuration);

        // Add services to the container.
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddSingleton<ManagementMetrics>();
        builder.Services.AddSingleton<IWorkerTickMetrics>(sp => sp.GetRequiredService<ManagementMetrics>());
        // Signs client (device) tokens for the socket host; accepts user tokens signed by the auth host,
        // whose public keys it fetches from Jwt:TrustedJwksUrl.
        builder.Services.AddWbsktTokenIssuer(JwtIssuers.Management);
        builder.Services.AddWbsktJwtTrust(JwtIssuers.Auth, JwtAudiences.Api);
        builder.Services.AddAccessTokenRevocation(builder.Configuration);
        // Device revocations, written before answering so a socket host that restarts or misses the
        // event still refuses the device; see ClientTokenCutoffs.
        builder.Services.AddClientTokenCutoffs(builder.Configuration);
        builder.Services.AddScoped<IRegistrationPolicyProvider, RegistrationPolicyProvider>();
        // These services publish after their change is committed, so they publish through the queued
        // bus: a broker outage must not turn a rotated secret or a registration into a 500 (see
        // QueuedEventBus). Commands and pings (ClientCommandService), and a run cancel sent to the engine
        // (IWorkflowEngineGateway), keep the real bus, because there the publish is the action.
        builder.Services.AddQueuedEventBus();
        builder.Services.AddScopedWithQueuedEvents<IRegistrationPolicyService, RegistrationPolicyService>();
        builder.Services.AddScoped<IClientProvider, ClientProvider>();
        builder.Services.AddScoped<ISocketHostProvider, SocketHostProvider>();
        builder.Services.AddScoped<IWorkspaceRetirementProvider, WorkspaceRetirementProvider>();
        // The one way a deleted workspace is retired: WorkspaceDeletedHandler and DeletedWorkspaceSweeper share it.
        builder.Services.AddScoped<WorkspaceRetirer>();
        builder.Services.AddScoped<IEventLogService, EventLogService>();
        builder.Services.AddScopedWithQueuedEvents<IClientRegistrationService, ClientRegistrationService>();
        builder.Services.AddScoped<IClientQueryService, ClientQueryService>();
        builder.Services.AddScopedWithQueuedEvents<IClientLifecycleService, ClientLifecycleService>();
        builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
        // Numeric state history: written by the state.report ingestion, read for charts and CSV.
        builder.Services.Configure<ReadingsOptions>(builder.Configuration.GetSection(ReadingsOptions.SectionName));
        builder.Services.AddScoped<IClientReadingProvider, ClientReadingProvider>();
        builder.Services.AddScoped<IClientReadingService, ClientReadingService>();
        if (role == HostRole.All)
        {
            builder.Services.AddHostedService<ClientReadingRetentionService>();
        }
        // Presence lasts only while the socket host holding it keeps sending heartbeats.
        builder.Services.Configure<PresenceOptions>(builder.Configuration.GetSection(PresenceOptions.SectionName));
        if (role == HostRole.All)
        {
            builder.Services.AddHostedService<StalePresenceSweeper>();
        }
        // The backstop behind WorkspaceDeletedEvent: daily, retires any workspace the auth host reports
        // deleted that still has live content here (an event lost, or one whose handling gave up).
        builder.Services.Configure<WorkspaceReconciliationOptions>(builder.Configuration.GetSection(WorkspaceReconciliationOptions.SectionName));
        if (role == HostRole.All)
        {
            builder.Services.AddHostedService<DeletedWorkspaceSweeper>();
        }
        builder.Services.AddScoped<IMessageTemplateProvider, MessageTemplateProvider>();
        builder.Services.AddScoped<IMessageTemplateService, MessageTemplateService>();
        builder.Services.AddScoped<IIntegrationProvider, IntegrationProvider>();
        builder.Services.AddScopedWithQueuedEvents<IIntegrationService, IntegrationService>();
        builder.Services.AddScoped<IntegrationReferenceCheck>();
        builder.Services.AddScoped<IWorkflowQueryService, WorkflowQueryService>();
        builder.Services.AddScopedWithQueuedEvents<IWorkflowLifecycleService, WorkflowLifecycleService>();
        builder.Services.AddScoped<IWorkflowRunQueryService, WorkflowRunQueryService>();
        builder.Services.AddScopedWithQueuedEvents<IWorkflowRunService, WorkflowRunService>();
        builder.Services.AddDeviceCommandSender();
        builder.Services.AddScoped<IClientCommandService, ClientCommandService>();
        builder.Services.AddScoped<IWorkflowHistoryService, WorkflowHistoryService>();
        builder.Services.AddScopedWithQueuedEvents<ISharedVariableService, SharedVariableService>();
        builder.Services.AddSingleton<WorkflowValidator>();
        
        builder.Services.AddTransient<AuthenticationForwardingHandler>();
        builder.Services.AddTransient<WorkflowEngineApiKeyHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<WorkspaceAccessCache>();
        builder.Services.AddSingleton<ClientFeedThrottle>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkspaceAccessCache>());
        builder.Services.AddHttpClient<IAuthServiceClient, AuthServiceClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
                                         ?? throw new ArgumentNullException(nameof(client.BaseAddress), "Services:Auth configuration is missing."));

            // Every management request waits on this call (or its cache). HttpClient's default of 100
            // seconds would let a stalled auth host hold every request that long; a resolve is one
            // stored procedure, so a few seconds is already generous.
            client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Services:AuthTimeoutSeconds", 5));
        })
        .AddHttpMessageHandler<AuthenticationForwardingHandler>();
        // The sweep's call to auth has no user token to forward, so it sends the shared internal key.
        builder.Services.AddTransient<AuthInternalApiKeyHandler>();
        builder.Services.AddHttpClient<IDeletedWorkspacesClient, DeletedWorkspacesClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
                                         ?? throw new ArgumentNullException(nameof(client.BaseAddress), "Services:Auth configuration is missing."));

            // A background job, not a request someone waits on: longer than the resolve's few seconds.
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<AuthInternalApiKeyHandler>();
        // A user token names its user by RefId; the id this host's records use comes from the auth host.
        builder.Services.AddUserIdLookup(sp => sp.GetRequiredService<IAuthServiceClient>());
        // WorkspacePermissionFilter resolves each workspace route through the auth host.
        builder.Services.AddScoped<IWorkspaceAccessResolver, AuthClientWorkspaceAccessResolver>();
        builder.Services.AddHttpClient<IWorkflowEngineClient, WorkflowEngineClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:WorkflowEngine"]
                                         ?? throw new ArgumentNullException(nameof(client.BaseAddress), "Services:WorkflowEngine configuration is missing."));

            // HttpClient's default of 100 seconds would hold a console request, or a public callback,
            // that long on a stalled engine. A failed call already maps to 503 with Retry-After.
            client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Services:WorkflowEngineTimeoutSeconds", 10));
        })
        .AddHttpMessageHandler<WorkflowEngineApiKeyHandler>();
        // Every engine operation goes through the gateway: start, signal, wake and webhook over the
        // client above, cancel as a command on the real bus (its send is the action, like a device
        // command). The engine client itself is only the gateway's transport.
        builder.Services.AddScoped<IWorkflowEngineGateway, WorkflowEngineGateway>();

        builder.Services.TryAddSingleton<IEventProvider, EventProvider>();
        builder.Services.AddWorkflowManagementServices(builder.Configuration);

        // Event Bus & Logging
        var dbEventLoggingBusConfig = builder.Services.AddDatabaseEventLogging(builder.Configuration);

        builder.Services.AddRabbitMqEventBus(builder.Configuration, 
            configureBus: configurator =>
            {
                dbEventLoggingBusConfig(configurator);

                // Register Auto SignalR Forwarding Consumers. Not on a Devices instance: it has no
                // hub, so it would only take broadcasts off the shared queue for nobody.
                if (role == HostRole.All)
                {
                    configurator.AddAutoSignalRForwarding();
                }
            },
            // The audit log retries whole batches on a longer schedule of its own.
            endpointsWithOwnRetry: [EventLoggerHandlerDefinition.QueueName],
            consumerAssemblies: [typeof(Program).Assembly]);

        // Commands and pings go straight to the bus, so they are attributed to their sender there; the
        // queued bus does the same for everything else (see ActorStampingEventBus).
        builder.Services.AddSingleton<IRequestOriginAccessor, HttpRequestOriginAccessor>();
        builder.Services.AddActorStampingEventBus();

        // Registered after the bus so it stops first, and can still send what is queued while stopping.
        builder.Services.AddHostedService<QueuedEventDispatcher>();

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        // The SignalR hub takes its token from ?access_token=, as browsers cannot set headers there.
        .AddWbsktJwtBearer(x => x.Events = new JwtBearerEvents().AcceptQueryToken("/hubs/notifications"));

        // Default-deny: the anonymous surface (health checks, client login and registration, the
        // public workflow callbacks) is an explicit [AllowAnonymous] on each.
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddDefaultDenyAuthorization();

        // Throttles for the anonymous edges: the public callback (see PublicCallbackRateLimits),
        // device enrollment (see ClientRegistrationsController) and device login (ClientAuthController).
        var callbackLimits = new PublicCallbackRateLimits(builder.Configuration);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectWithProblem();
            options.AddPolicy(PublicCallbackPolicy.RateLimitPolicy, callbackLimits.ForTarget);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(callbackLimits.ForAddress);

            // Device enrollment. A fleet being provisioned from one site shares an address, so the
            // budget is per IP and configurable; the default still makes walking the PIN space hopeless.
            options.AddPerIpPolicy(builder.Configuration, RateLimitPolicies.DeviceRegistration, "DeviceRegistration", defaultPermitLimit: 20);

            // Device login. Same per-IP partitioning, with room for a fleet behind one address.
            options.AddPerIpPolicy(builder.Configuration, RateLimitPolicies.DeviceLogin, "DeviceLogin", defaultPermitLimit: 300);

            // Integration tests, per workspace: generous for someone setting one up, a wall for a loop.
            int testPermitLimit = builder.Configuration.GetValue("RateLimiting:IntegrationTest:PermitLimit", 20);
            var testWindow = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:IntegrationTest:WindowMinutes", 1));
            options.AddPolicy(RateLimitPolicies.IntegrationTest, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // Normalised: the route accepts any GUID spelling (case, braces, no dashes), and
                    // each raw spelling would otherwise get a window of its own.
                    partitionKey: Guid.TryParse(httpContext.GetRouteValue("workspaceRef")?.ToString(), out var workspaceRef) ? workspaceRef.ToString("N") : "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = testPermitLimit, Window = testWindow, QueueLimit = 0 }));
        });

        // Signed-in callers: a budget per user (see UserRateLimiter), after authentication.
        builder.Services.AddUserRateLimit(builder.Configuration);

        // X-Total-Count is exposed, or the console (always a separate origin) could not read the
        // list endpoints' count and paged lists would silently look complete.
        builder.Services.AddWbsktCors(builder.Configuration, builder.Environment, "X-Total-Count");
        builder.Services.AddWbsktForwardedHeaders(builder.Configuration);

        // Readiness probes. Redis is deliberately not among them: it backs the SignalR fan-out
        // across scaled instances, so losing it degrades cross-instance delivery rather than
        // stopping this instance serving - and because readiness now drives Traefik's routing
        // table, a probe on a non-essential dependency could take the API offline for a fault
        // that does not warrant it. The bus check comes from AddMassTransit.
        builder.Services.AddHealthChecks().AddSqlServerCheck("DefaultConnection");

        // Workspace-scoped actions declare their permissions with [RequiresPermission]; this filter
        // resolves the workspace once and refuses the request before it reaches the action.
        builder.Services.AddControllers(options => options.Filters.Add<WorkspacePermissionFilter>()).ConfigureApplicationPartManager(manager =>
        {
            manager.FeatureProviders.Remove(manager.FeatureProviders.OfType<ControllerFeatureProvider>().Single());
            manager.FeatureProviders.Add(new HostRoleControllerFeatureProvider(role));
        });
        builder.Services.AddWbsktJson();
        var signalRBuilder = builder.Services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { IgnoreSignalRPrivateProperties }
            };
            WbsktJsonExtensions.Apply(options.PayloadSerializerOptions);
        });

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // Backplane for scale-out: signalr-forwarder-* consumers stay a shared/competing queue
            // (one consume -> one IHubContext.Group broadcast), the backplane then fans that
            // broadcast out to every instance's locally-connected clients.
            signalRBuilder.AddStackExchangeRedis(redisConnectionString);
        }
        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        // The rate limiter runs before authentication, so anonymous callback floods are refused
        // before they reach auth or the engine relay.
        app.UseUnversionedApiAliases();
        app.UseWbsktPipeline();

        if (app.Configuration.IsApiDocsEnabled(app.Environment))
        {
            // API docs (on in Development, opt-in elsewhere via ApiDocs:Enabled) are exempt from the
            // default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();

            app.MapCustomScalarApiReference();
        }

        app.MapWbsktHealthChecks();
        app.MapWbsktJwks();

        app.Logger.LogInformation("Serving as host role {HostRole}", role);

        app.MapControllers();
        if (role == HostRole.All)
        {
            // A connection is authorised once, when it opens, and JoinWorkspace checks permission once,
            // so without this a signed-out user or a removed member kept the feed for as long as the
            // socket stayed up. Closing it when the token expires makes the client reconnect with a
            // fresh token, and its rejoin runs the permission check again.
            app.MapHub<NotificationHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);
        }

        await app.RunAsync();
    }

    private static void IgnoreSignalRPrivateProperties(JsonTypeInfo typeInfo)
    {
        // 1. Quick Exit: Only process objects that implement IEvent
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IEvent).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }
        
        // 2. Scan properties
        foreach (var propertyInfo in typeInfo.Properties)
        {
            // Check property itself
            var hasPrivateAttribute = propertyInfo.AttributeProvider?.GetCustomAttributes(typeof(SignalRPrivateAttribute), false).Length > 0;
            
            // Check interfaces (only if not already found)
            if (!hasPrivateAttribute)
            {
                hasPrivateAttribute = typeInfo.Type.GetInterfaces()
                    .Select(i => i.GetProperty(propertyInfo.Name))
                    .Any(p => p != null && p.GetCustomAttributes(typeof(SignalRPrivateAttribute), false).Length > 0);
            }
            
            if (hasPrivateAttribute)
            {
                propertyInfo.ShouldSerialize = (_, _) => false;
            }
        }
    }
}