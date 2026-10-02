using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.OpenTelemetry;
using Wbskt.Infrastructure.Telemetry;

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
        var configuration = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration);

        // Alongside the console, not instead of it: `docker compose logs` keeps working when the
        // collector is down, and the collector gets every event with the trace and span ids of the
        // request that wrote it, so a trace in Grafana links straight to its logs.
        if (builder.Configuration.ExportsTelemetry())
        {
            configuration.WriteTo.OpenTelemetry(options =>
            {
                // OTEL_EXPORTER_OTLP_ENDPOINT is a base URL; over HTTP the sink wants the logs path itself.
                var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]!.TrimEnd('/');
                var http = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] == "http/protobuf";
                options.Endpoint = http ? endpoint + "/v1/logs" : endpoint;
                options.Protocol = http ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;
                options.ResourceAttributes["service.name"] = builder.ServiceName();
                options.ResourceAttributes["service.instance.id"] = Environment.MachineName;
                options.ResourceAttributes["deployment.environment.name"] = builder.Environment.EnvironmentName;
            });
        }

        return configuration.CreateLogger();
    }
}