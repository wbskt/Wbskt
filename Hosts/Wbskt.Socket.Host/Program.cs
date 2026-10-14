using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Socket.Host.HostedServices;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Services;
using Wbskt.Socket.Host.Services.Inbound;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Socket.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "rabbitmq.json");
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
        // What a device sends: one handler per protocol message type, and the device's own messages.
        builder.Services.AddSingleton<IInboundMessageHandler, PongHandler>();
        builder.Services.AddSingleton<IInboundMessageHandler, AckHandler>();
        builder.Services.AddSingleton<DeviceMessageHandler>();
        builder.Services.AddSingleton<InboundMessageDispatcher>();
        builder.Services.Configure<MessageBudgetOptions>(builder.Configuration.GetSection(MessageBudgetOptions.SectionName));

        // Event Bus — per-instance fan-out so every socket instance sees every client event
        // (ClientCommandEvent, ClientPingEvent, ClientStatusChangedEvent); see EventBusExtensions.
        builder.Services.AddRabbitMqEventBus(builder.Configuration, perInstanceEndpoints: true, consumerAssemblies: [typeof(Program).Assembly]);

        // Background services (registered after the bus so it starts first)
        builder.Services.AddHostedService<HeartbeatPublisher>();
        builder.Services.AddHostedService<PingSampler>();

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        // Device tokens through the framework's bearer handler, from the Authorization header or, for
        // browsers, which cannot set headers on a WebSocket, from ?access_token= on /ws.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddWbsktJwtBearer(x => x.Events = new JwtBearerEvents().AcceptQueryToken("/ws"));

        // Default-deny, so that a controller added later cannot ship anonymous by accident.
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddDefaultDenyAuthorization(options =>
        {
            // Issuer and audience already rule out a user token (only the management host signs for
            // wbskt-socket); the typ claim stays as a second, independent guard.
            options.AddPolicy(ClientClaims.ClientOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ClientClaims.IsClient(context.User)));
        });

        // Bus only: this host holds no database connection. The check itself comes from
        // AddMassTransit (masstransit-bus, tagged "ready").
        builder.Services.AddHealthChecks();

        // No controllers and so no API docs: the device protocol on /ws is this host's whole API.
        builder.Services.AddWbsktJson();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseWbsktPipeline();

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

        app.Map("/ws", async (HttpContext context, ISocketHandler handler) =>
        {
            await handler.HandleAsync(context);
        }).RequireAuthorization(ClientClaims.ClientOnlyPolicy);

        app.MapWbsktHealthChecks();

        await app.RunAsync();
    }
}
