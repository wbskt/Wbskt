using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Socket.Host.Extensions;
using Wbskt.Socket.Host.HostedServices;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Middleware;
using Wbskt.Socket.Host.Services;

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
        
        builder.AddSharedConfiguration("serilog.json", "rabbitmq.json", "jwt.json");

        builder.Host.UseSerilog(builder.CreateSerilog());
        
        // Add services to the container.
        builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
        builder.Services.AddSingleton<IRevocationCache, RevocationCache>();
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<ISocketHandler, SocketHandler>();

        // Event Bus — per-instance fan-out so every socket instance sees every client event
        // (ClientCommandEvent, ClientPingEvent, ClientStatusChangedEvent); see EventBusExtensions.
        builder.Services.AddRabbitMqEventBus(builder.Configuration, perInstanceEndpoints: true);

        // Background services (registered after the bus so it starts first)
        builder.Services.AddHostedService<StartupPresenceResetPublisher>();
        builder.Services.AddHostedService<PingSampler>();

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthorization();

        builder.Services.AddControllers();
        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapCustomScalarApiReference();
        }

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        });

        // Custom WebSocket Authentication Middleware
        app.UseMiddleware<WebSocketAuthMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        app.Map("/ws", async (HttpContext context, ISocketHandler handler) =>
        {
            await handler.HandleAsync(context);
        });

        app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();
        app.MapControllers();

        await app.RunAsync();
    }
}
