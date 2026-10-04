using System.Net;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Extensions;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Extensions;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Management.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        
        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json");

        builder.Host.UseSerilog(builder.CreateSerilog());
        builder.AddWbsktTelemetry();

        // Add services to the container.
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
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
        // QueuedEventBus). Commands and pings in ClientsController keep the real bus, because there
        // the publish is the action.
        builder.Services.AddQueuedEventBus();
        builder.Services.AddScopedWithQueuedEvents<IRegistrationPolicyService, RegistrationPolicyService>();
        builder.Services.AddScoped<IClientProvider, ClientProvider>();
        builder.Services.AddScoped<IWorkspaceRetirementProvider, WorkspaceRetirementProvider>();
        builder.Services.AddScoped<IEventLogService, EventLogService>();
        builder.Services.AddScopedWithQueuedEvents<IClientRegistrationService, ClientRegistrationService>();
        builder.Services.AddScopedWithQueuedEvents<IClientService, ClientService>();
        builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
        builder.Services.AddScoped<IMessageTemplateProvider, MessageTemplateProvider>();
        builder.Services.AddScoped<IMessageTemplateService, MessageTemplateService>();
        builder.Services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService>();
        builder.Services.AddScoped<IWorkflowRunQueryService, WorkflowRunQueryService>();
        builder.Services.AddSingleton<WorkflowValidator>();
        
        builder.Services.AddTransient<AuthenticationForwardingHandler>();
        builder.Services.AddTransient<WorkflowEngineApiKeyHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<WorkspaceAccessCache>();
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
        builder.Services.AddHttpClient<IWorkflowEngineClient, WorkflowEngineClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:WorkflowEngine"]
                                         ?? throw new ArgumentNullException(nameof(client.BaseAddress), "Services:WorkflowEngine configuration is missing."));
        })
        .AddHttpMessageHandler<WorkflowEngineApiKeyHandler>();

        builder.Services.TryAddSingleton<IEventProvider, EventProvider>();
        builder.Services.AddWorkflowManagementServices(builder.Configuration);

        // Event Bus & Logging
        var dbEventLoggingBusConfig = builder.Services.AddDatabaseEventLogging(builder.Configuration);

        builder.Services.AddRabbitMqEventBus(builder.Configuration, 
            configureBus: configurator =>
            {
                dbEventLoggingBusConfig(configurator);

                // Register Auto SignalR Forwarding Consumers
                configurator.AddAutoSignalRForwarding();
            });

        // Registered after the bus so it stops first, and can still send what is queued while stopping.
        builder.Services.AddHostedService<QueuedEventDispatcher>();

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IRegistrationPolicyProvider>>(ReferenceType.RegistrationPolicy);
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IClientProvider>>(ReferenceType.Client);

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddWbsktJwtBearer(x =>
        {
            x.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var isSignalR = context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications");
                    if (!isSignalR)
                    {
                        return Task.CompletedTask;
                    }

                    var accessToken = context.Request.Query["access_token"];
                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        return Task.CompletedTask;
                    }

                    context.Token = accessToken;

                    return Task.CompletedTask;

                }
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            // Default-deny: every endpoint requires an authenticated user unless it explicitly opts
            // out with [AllowAnonymous] (health checks, client login/registration, and the public
            // workflow callbacks). This makes the anonymous surface an explicit, auditable choice
            // rather than the default that any [Authorize]-less controller silently inherits.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        // Per-IP throttles for the anonymous edges: the public callback (see PublicCallbackController)
        // and device enrollment (see ClientRegistrationsController).
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PublicCallbackPolicy.RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PublicCallbackPolicy.PermitsPerWindow,
                        Window = TimeSpan.FromSeconds(PublicCallbackPolicy.RateLimitWindowSeconds),
                        QueueLimit = 0
                    }));

            // Device enrollment. A fleet being provisioned from one site shares an address, so the
            // budget is per IP and configurable; the default still makes walking the PIN space hopeless.
            options.AddPolicy(RateLimitPolicies.DeviceRegistration, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:DeviceRegistration:PermitLimit", 20),
                        Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:DeviceRegistration:WindowMinutes", 1)),
                        QueueLimit = 0
                    }));
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policyBuilder =>
            {
                var allowedOrigins = builder.Configuration.GetCorsAllowedOrigins();
                if (allowedOrigins is { Length: > 0 })
                {
                    policyBuilder.SetIsOriginAllowed(allowedOrigins.IsCorsOriginAllowed).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
                }
                else if (builder.Environment.IsDevelopment())
                {
                    policyBuilder.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
                }

                // AllowAnyHeader covers request headers only. Without this the list endpoints'
                // X-Total-Count is dropped before script can read it - the console is always a
                // separate origin (nginx serves the SPA and never proxies the API), so paged
                // lists would silently look complete.
                policyBuilder.WithExposedHeaders("X-Total-Count");
            });
        });

        // Readiness probes. Redis is deliberately not among them: it backs the SignalR fan-out
        // across scaled instances, so losing it degrades cross-instance delivery rather than
        // stopping this instance serving - and because readiness now drives Traefik's routing
        // table, a probe on a non-essential dependency could take the API offline for a fault
        // that does not warrant it. The bus check comes from AddMassTransit.
        builder.Services.AddHealthChecks().AddSqlServerCheck("DefaultConnection");

        builder.Services.AddControllers();
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

        // A nested `KnownProxies = { }` initializer is a no-op (it Adds nothing, the loopback
        // defaults survive) - the lists must be populated explicitly or Traefik's X-Forwarded-*
        // headers get silently ignored. Trust Traefik's own pinned addresses specifically rather
        // than clearing the lists (which would trust X-Forwarded-* from any container reachable
        // on the docker network, not just Traefik). These addresses must match the static
        // ipv4_address pinned to the traefik service on the edge/backend networks in
        // deploy/compose/docker-compose.yml.
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse("172.28.0.2"));
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse("172.28.1.2"));
        app.UseForwardedHeaders(forwardedHeadersOptions);

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseCors("AllowAll");

        // Reject anonymous callback floods before they reach auth or the engine relay. Placed after
        // UseForwardedHeaders so the partition key is the real client IP behind Traefik.
        app.UseRateLimiter();

        if (app.Configuration.IsApiDocsEnabled(app.Environment))
        {
            // API docs (on in Development, opt-in elsewhere via ApiDocs:Enabled) are exempt from the
            // default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();

            app.MapCustomScalarApiReference();
        }

        app.UseAuthentication();
        app.UseMiddleware<IdentityMiddleware>();
        app.UseAuthorization();
        app.MapWbsktHealthChecks();
        app.MapWbsktJwks();

        app.MapControllers();
        app.MapHub<NotificationHub>("/hubs/notifications");

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