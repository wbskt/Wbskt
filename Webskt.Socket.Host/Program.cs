using Scalar.AspNetCore;
using Serilog;
using Webskt.Common.Abstraction.Constants;
using Webskt.Common.Logging;
using Webskt.Common.Middlewares;
using Webskt.Common.Security;
using Webskt.Socket.Host.Infrastructure;
using Webskt.Socket.Host.Middleware;
using Webskt.Socket.Host.Services;

namespace Webskt.Socket.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        if (!Directory.Exists(ProgramDataPath))
        {
            Directory.CreateDirectory(ProgramDataPath);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.Host.UseSerilog(builder.CreateSerilog());

        // Add services to the container.
        builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<ISocketHandler, SocketHandler>();

        builder.Services.AddAuthorization();
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
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

        app.MapControllers();

        app.Run();
    }
}
