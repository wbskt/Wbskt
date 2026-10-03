using Microsoft.AspNetCore.Authorization;
using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Socket.Host.Extensions;
using Wbskt.Socket.Host.HostedServices;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Middleware;
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
        // Signs nothing; accepts client tokens signed by the management host (Jwt:TrustedJwksUrl).
        builder.Services.AddWbsktJwtTrust(JwtIssuers.Management, JwtAudiences.Socket);
        builder.Services.AddScoped<ISocketHandler, SocketHandler>();

        // Event Bus — per-instance fan-out so every socket instance sees every client event
        // (ClientCommandEvent, ClientPingEvent, ClientStatusChangedEvent); see EventBusExtensions.
        builder.Services.AddRabbitMqEventBus(builder.Configuration, perInstanceEndpoints: true);

        // Background services (registered after the bus so it starts first)
        builder.Services.AddHostedService<StartupPresenceResetPublisher>();
        builder.Services.AddHostedService<PingSampler>();

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthorization(options =>
        {
            // Default-deny, matching the management host. This host has no controllers today, so it
            // changes nothing now; it exists so that the first one added cannot ship anonymous by
            // accident. /ws opts out explicitly below - see the note there.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        // Bus only: this host holds no database connection. The check itself comes from
        // AddMassTransit (masstransit-bus, tagged "ready").
        builder.Services.AddHealthChecks();

        builder.Services.AddControllers();
        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (app.Configuration.IsApiDocsEnabled(app.Environment))
        {
            // API docs (on in Development, opt-in elsewhere via ApiDocs:Enabled) are exempt from the
            // default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();
            app.MapCustomScalarApiReference();
        }

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

        // Custom WebSocket Authentication Middleware
        app.UseMiddleware<WebSocketAuthMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        // Exempt from the fallback policy because this route authenticates itself, earlier in the
        // pipeline: WebSocketAuthMiddleware validates the token, rejects anything that is not a
        // client token, and assigns context.User before UseAuthentication runs. Letting the
        // authorization pipeline also gate it would make the browser upgrade path - which carries
        // its token in ?access_token= rather than a header, so the JWT bearer handler never sees
        // one - depend on that middleware-assigned principal surviving UseAuthentication. It does
        // survive today, but nothing enforces that, and the failure mode is every browser client
        // silently failing to connect.
        app.Map("/ws", async (HttpContext context, ISocketHandler handler) =>
        {
            await handler.HandleAsync(context);
        }).AllowAnonymous();

        app.MapWbsktHealthChecks();
        app.MapControllers();

        await app.RunAsync();
    }
}
