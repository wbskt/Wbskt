using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.OpenTelemetry;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Infrastructure.Configuration;

public static class SharedConfigurationExtension
{
    /// <summary>
    /// Adds the shared configuration files (from the repo's <c>Config</c> folder, then the host's own
    /// copy) beneath the builder's own sources: appsettings, user secrets, environment variables and the
    /// command line all still win over them, in that order.
    /// </summary>
    public static IHostApplicationBuilder AddSharedConfiguration(
        this IHostApplicationBuilder builder,
        params string[] fileNames)
    {
        var sharedConfigPath = Path.Combine(
            builder.Environment.ContentRootPath, "..", "..", "Config");

        var shared = new List<IConfigurationSource>();
        foreach (var fileName in fileNames)
        {
            shared.Add(JsonFile(Path.Combine(sharedConfigPath, fileName)));
            shared.Add(JsonFile(Path.Combine(builder.Environment.ContentRootPath, fileName)));
        }

        // Before appsettings.json, so everything the builder added after it keeps its precedence.
        // Appending them, as this once did, put them above the command line.
        var sources = builder.Configuration.Sources;
        int at = sources.Select((source, index) => (source, index))
            .FirstOrDefault(s => s.source is JsonConfigurationSource { Path: "appsettings.json" }, (null!, 0)).index;
        for (int i = 0; i < shared.Count; i++)
        {
            sources.Insert(at + i, shared[i]);
        }

        return builder;
    }

    // Read once at start: a file edited under a running host should not change it half-way.
    private static JsonConfigurationSource JsonFile(string path)
    {
        var source = new JsonConfigurationSource { Path = Path.GetFullPath(path), Optional = true, ReloadOnChange = false };
        source.ResolveFileProvider();
        return source;
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