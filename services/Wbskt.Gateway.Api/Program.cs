using Serilog;
using Wbskt.Common;

namespace Wbskt.Gateway.Api;

internal static class Program
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
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogName, typeof(Program).Assembly.FullName);

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
        var proxyConfig = builder.Configuration.GetSection("ReverseProxy");
        builder.Services.AddReverseProxy().LoadFromConfig(proxyConfig);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseHttpsRedirection();
        app.UseHsts();

        app.MapReverseProxy();

        app.Run();
    }
}

