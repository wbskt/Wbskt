using Wbskt.Gateway.Core;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Gateway.MQTT.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Primitives;

namespace Wbskt.Gateway.MQTT.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "rabbitmq.json");
        builder.AddWbsktTelemetry(GatewayMetrics.MeterName);

        // The client sessions, the bus and its consumers, and the presence and latency services.
        // This host adds only the MQTT transport.
        builder.Services.AddGatewayCore(builder.Configuration, GatewayTransport.Mqtt);
        builder.Services.Configure<MqttGatewayOptions>(builder.Configuration.GetSection(MqttGatewayOptions.SectionName));
        builder.Services.AddSingleton<MqttNetLoggerBridge>();
        builder.Services.AddSingleton<MqttConnectAuthenticator>();
        builder.Services.AddSingleton<MqttGateway>();
        // Registered after the bus (in AddGatewayCore), so it stops first and sessions can still
        // announce their end as the host shuts down.
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttGateway>());

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        // HTTP carries only health checks and metrics: clients authenticate in their MQTT CONNECT.
        // Default-deny, so that an endpoint added later cannot ship anonymous by accident.
        builder.Services.AddAuthentication();
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddDefaultDenyAuthorization();

        // Bus only: this host holds no database connection. The check itself comes from
        // AddMassTransit (masstransit-bus, tagged "ready").
        builder.Services.AddHealthChecks();
        builder.Services.AddWbsktJson();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseWbsktPipeline();
        app.MapWbsktHealthChecks();

        await app.RunAsync();
    }
}
