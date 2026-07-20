using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace Wbskt.Infrastructure.Configuration;

public static class SharedConfigurationExtension
{
    public static IHostApplicationBuilder AddSharedConfiguration(
        this IHostApplicationBuilder builder,
        params string[] fileNames)
    {
        var sharedConfigPath = Path.Combine(
            builder.Environment.ContentRootPath, "..", "..", "Config");

        foreach (var fileName in fileNames)
        {
            builder.Configuration.AddJsonFile(
                Path.Combine(sharedConfigPath, fileName),
                optional: true,
                reloadOnChange: true);

            builder.Configuration.AddJsonFile(
                Path.Combine(builder.Environment.ContentRootPath, fileName),
                optional: true,
                reloadOnChange: true);
        }

        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

        // CreateBuilder already adds env vars, but at lower precedence than the JSON providers
        // above. Re-append so container env vars (secrets, per-instance overrides) win.
        builder.Configuration.AddEnvironmentVariables();

        return builder;
    }
    
    public static Logger CreateSerilog(this IHostApplicationBuilder builder)
    {
        return new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
    }
}