using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wbskt.Gateway.Core;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Socket.Host.Services;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Socket.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "rabbitmq.json");
        builder.AddWbsktTelemetry(GatewayMetrics.MeterName);

        // The device sessions, the bus and its consumers, and the presence and latency services.
        // This host adds only the WebSocket transport.
        builder.Services.AddGatewayCore(builder.Configuration, GatewayTransport.WebSocket);
        builder.Services.AddScoped<ISocketHandler, SocketHandler>();

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
