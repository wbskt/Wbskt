using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Wbskt.Infrastructure.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>MassTransit's ActivitySource and Meter. Its spans carry the trace across the bus.</summary>
    private const string MassTransitSource = "MassTransit";

    /// <summary>
    /// The name this host reports as: <c>OTEL_SERVICE_NAME</c> (set per service in compose), else the
    /// assembly name. Shared by traces, metrics and logs so all three line up in Grafana.
    /// </summary>
    public static string ServiceName(this IHostApplicationBuilder builder) =>
        builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } name ? name : builder.Environment.ApplicationName;

    /// <summary>Whether <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> names a collector to send telemetry to.</summary>
    public static bool ExportsTelemetry(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    /// <summary>
    /// Traces and metrics over OTLP to the collector: inbound HTTP, outbound HttpClient, the bus
    /// (MassTransit propagates the W3C trace context in message headers, so a trace runs from the
    /// console's request through the bus into the engine), the runtime, and <paramref name="meters"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is registered without a collector: there is nowhere to send it, and a host run from a
    /// checkout should not need one. Endpoint, protocol and headers are the standard <c>OTEL_*</c>
    /// variables, read by the exporter itself.
    /// </remarks>
    public static IHostApplicationBuilder AddWbsktTelemetry(this IHostApplicationBuilder builder, params string[] meters)
    {
        if (!builder.Configuration.ExportsTelemetry())
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(builder.ServiceName(), serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation()
                .AddSource(MassTransitSource))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(MassTransitSource)
                .AddMeter(meters))
            .UseOtlpExporter();

        return builder;
    }

    // Health probes run every few seconds per container; tracing them would bury everything else.
    private static bool IsProbe(PathString path) => path.StartsWithSegments("/healthz");
}
