using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace Wbskt.Common.Logging;

public static class SerilogConfigurationExtension
{
    public static Logger CreateSerilog(this IHostApplicationBuilder builder)
    {
        // Configure Serilog
        var serilogInBinConfigPath = Path.Combine(builder.Environment.ContentRootPath, "serilog.json");
        var serilogConfigPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "Config", "serilog.json");

        // Load the shared configuration from the central Config folder
        builder.Configuration.AddJsonFile(serilogConfigPath, optional: true, reloadOnChange: true);

        // Load the local configuration from the bin folder, overriding any shared settings
        builder.Configuration.AddJsonFile(serilogInBinConfigPath, optional: true, reloadOnChange: true);

        // Apply environment-specific overrides (e.g., Development or Production specific settings)
        builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

        return new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
    }
}