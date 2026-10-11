using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Socket.Host.HostedServices;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Services;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Socket.Host;

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
        
        builder.AddSharedConfiguration("serilog.json", "rabbitmq.json");

        builder.Host.UseSerilog(builder.CreateSerilog());
        builder.Services.AddSingleton<SocketMetrics>();
        builder.AddWbsktTelemetry(SocketMetrics.MeterName);
        
        // Add services to the container.
        builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
        builder.Services.AddSingleton<IRevocationCache, RevocationCache>();
        // The durable copy of the same revocations, written by the management host; see ClientTokenCutoffs.
        builder.Services.AddClientTokenCutoffs(builder.Configuration);
        builder.Services.AddSingleton<IClientAccessGate, ClientAccessGate>();
        // Signs nothing; accepts client tokens signed by the management host (Jwt:TrustedJwksUrl).
        builder.Services.AddWbsktJwtTrust(JwtIssuers.Management, JwtAudiences.Socket);
        builder.Services.AddScoped<ISocketHandler, SocketHandler>();
        builder.Services.Configure<MessageBudgetOptions>(builder.Configuration.GetSection(MessageBudgetOptions.SectionName));

        // Event Bus — per-instance fan-out so every socket instance sees every client event
        // (ClientCommandEvent, ClientPingEvent, ClientStatusChangedEvent); see EventBusExtensions.
        builder.Services.AddRabbitMqEventBus(builder.Configuration, perInstanceEndpoints: true);

        // Background services (registered after the bus so it starts first)
        builder.Services.AddHostedService<StartupPresenceResetPublisher>();
        builder.Services.AddHostedService<HeartbeatPublisher>();
        builder.Services.AddHostedService<PingSampler>();

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        // Device tokens through the framework's bearer handler, from the Authorization header or, for
        // browsers, which cannot set headers on a WebSocket, from ?access_token= on /ws.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddWbsktJwtBearer(x => x.Events = new JwtBearerEvents().AcceptQueryToken("/ws"));

        builder.Services.AddAuthorization(options =>
        {
            // Default-deny, matching the management host, so that a controller added later cannot
            // ship anonymous by accident.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            // Issuer and audience already rule out a user token (only the management host signs for
            // wbskt-socket); the type claim stays as a second, independent guard.
            options.AddPolicy(ClientClaims.ClientOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClientClaims.Type, ClientClaims.ClientType));
        });

        // Bus only: this host holds no database connection. The check itself comes from
        // AddMassTransit (masstransit-bus, tagged "ready").
        builder.Services.AddHealthChecks();

        // No controllers and so no API docs: the device protocol on /ws is this host's whole API.
        builder.Services.AddWbsktJson();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        // The server pings every interval and drops a connection that has not answered within the
        // timeout. Without the timeout, a device that loses power or Wi-Fi leaves a half-open socket
        // that keeps it "online" (and its offline triggers silent) until TCP itself gives up, which
        // can take many minutes. Client libraries answer pings on their own; a client that cannot
        // can be accommodated by raising the timeout, or setting it to 0 to turn the check off.
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(app.Configuration.GetValue("WebSockets:KeepAliveIntervalSeconds", 30)),
            KeepAliveTimeout = TimeSpan.FromSeconds(app.Configuration.GetValue("WebSockets:KeepAliveTimeoutSeconds", 30))
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.Map("/ws", async (HttpContext context, ISocketHandler handler) =>
        {
            await handler.HandleAsync(context);
        }).RequireAuthorization(ClientClaims.ClientOnlyPolicy);

        app.MapWbsktHealthChecks();

        await app.RunAsync();
    }
}
