using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Devices;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Primitives;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Engine.Host.HealthChecks;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Engine.Host.Middleware;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "connectionstrings.json", "rabbitmq.json", "workflow-engine.json");
        builder.AddWbsktTelemetry(WorkflowMetrics.MeterName);

        builder.Services.AddHttpClient();
        // Outbound WebhookNotification client: don't follow redirects, so a 3xx to an internal
        // address can't sidestep the OutboundAddressGuard SSRF check on the original target.
        builder.Services.AddHttpClient("workflow-webhook")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        // Every call an integration makes (Telegram, Discord, ntfy, an HTTP auth test): no redirects,
        // so a 3xx cannot carry a credential, or the request, past the checks on where it may go.
        builder.Services.AddHttpClient(HttpChannelSupport.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();
        builder.Services.AddEngineHostServices(builder.Configuration);
        builder.Services.AddSingleton<IWorkerTickMetrics>(sp => sp.GetRequiredService<WorkflowMetrics>());
        builder.Services.AddRabbitMqEventBus(builder.Configuration, consumerAssemblies: [typeof(Program).Assembly]);
        // The bus is started manually by EngineLeadershipCoordinator once this instance is leader,
        // so a standby never attaches to the shared engine queues.
        builder.Services.RemoveMassTransitHostedService();

        // Readiness gates traffic to the current leader: Traefik's load-balancer health check
        // already points at /healthz/ready, and a standby must never receive an inbound trigger.
        // The bus check comes from AddMassTransit and is unhealthy on a standby anyway, but
        // leadership is asserted explicitly rather than inferred from it.
        builder.Services.AddHealthChecks()
            .AddSqlServerCheck("DefaultConnection")
            .AddCheck<LeadershipHealthCheck>("engine-leadership", tags: [HealthCheckExtensions.ReadyTag]);

        builder.Services.AddAuthorization();
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddControllers();
        builder.Services.AddWbsktJson();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseWbsktPipeline();
        app.UseMiddleware<InboundApiKeyMiddleware>();
        app.UseMiddleware<LeaderOnlyMiddleware>();
        app.MapWbsktHealthChecks();
        app.MapControllers();

        await app.RunAsync();
    }

    /// <summary>
    /// The engine's own services: the workflow runtime, the host's adapters and the background workers.
    /// Separate from Main so a test can build exactly what the host builds and validate it.
    /// </summary>
    public static IServiceCollection AddEngineHostServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWorkflowEngine(configuration);
        // Re-registers ILeadershipGate (AddWorkflowEngine defaults to always-active for
        // tests/Management) - last registration wins, so the engine host resolves the real one.
        services.AddSingleton<ILeadershipGate, LeadershipGate>();
        services.AddDeviceCommandSender();
        services.AddScoped<IClientPresenceCheckProvider, ClientPresenceCheckProvider>();
        services.AddScoped<ClientPresenceParker>();
        services.AddScoped<IClientHoldStateProvider, ClientHoldStateProvider>();
        services.AddScoped<ClientHoldRecorder>();
        services.AddScoped<IDeviceCommandPublisher, DeviceCommandPublisher>();
        services.AddScoped<IToastPublisher, ToastPublisher>();
        services.AddScoped<IVariableWritePublisher, VariableWritePublisher>();
        services.AddScoped<IRunStartedPublisher, EventBusRunStartedPublisher>();
        services.AddScoped<IRunCompletedPublisher, EventBusRunCompletedPublisher>();
        services.AddSingleton<RunRecoveryService>();
        services.AddSingleton<IEngineStartupTracker>(sp => sp.GetRequiredService<RunRecoveryService>());
        // LeaderElectionService is registered first so LeadershipState is meaningful before any
        // other hosted service starts checking ILeadershipGate.IsActive. RunRecoveryService is
        // deliberately NOT a hosted service here - EngineLeadershipCoordinator calls its
        // RecoverAsync directly, only once this instance becomes leader.
        services.AddHostedService<LeaderElectionService>();
        services.AddHostedService<EngineLeadershipCoordinator>();
        services.AddHostedService<BranchExecutionPump>();
        services.AddHostedService<BookmarkScheduler>();
        services.AddHostedService<BookmarkOrphanGc>();
        services.AddHostedService<ScheduledFireTicker>();
        services.AddHostedService<ClientPresenceTicker>();
        services.AddHostedService<ClientHoldTicker>();
        services.AddHostedService<RunReaper>();
        services.AddHostedService<HistoryRetentionGc>();
        services.AddHostedService<IdempotencyKeyGc>();
        services.AddHostedService<PendingTriggerEventBacklogReaper>();
        services.AddHostedService<MetricsExporter>();

        return services;
    }
}
