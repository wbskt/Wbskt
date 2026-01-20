using Serilog;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Socket.Api.HostedServices;
using Wbskt.Socket.Api.Services;

namespace Wbskt.Socket.Api;

internal static  class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        var programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Application.AppFolderName);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogPath, programDataPath);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogName, typeof(Program).Namespace);

        if (!Directory.Exists(programDataPath))
        {
            Directory.CreateDirectory(programDataPath);
        }

        // Configure Serilog
        var serilogConfigPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "Config", "serilog.json");
        builder.Configuration.AddJsonFile(serilogConfigPath, optional: false, reloadOnChange: true);
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Host.UseSerilog(Log.Logger);

        // Add services to the container.
        builder.Services.ConfigureCommonServices();
        builder.Services.AddSingleton<IClientConnectionManager, ClientConnectionManager>();

        builder.Services.AddSingleton<SendCommandToClientEventHandler>();

        builder.Services.AddWbsktAuthentication(builder.Configuration);

        builder.Services.AddControllers();
        builder.Services.AddHostedService<CommandListenerService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseWebSockets();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}
