using MassTransit;
using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Engine.Host.Extensions;
using Wbskt.Workflow.Engine.Host.HealthChecks;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Engine.Host.Middleware;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json", "workflow-engine.json");

        builder.Host.UseSerilog(builder.CreateSerilog());
        builder.AddWbsktTelemetry(WorkflowMetrics.MeterName);

        builder.Services.AddHttpClient();
        // Outbound WebhookNotification client: don't follow redirects, so a 3xx to an internal
        // address can't sidestep the OutboundAddressGuard SSRF check on the original target.
        builder.Services.AddHttpClient("workflow-webhook")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        // Telegram goes to a fixed, configured API base rather than an author-supplied URL, so it needs
        // no address guard - but it gets its own client so its timeout and handler lifetime are not
        // shared with arbitrary author-controlled webhook targets.
        builder.Services.AddHttpClient("workflow-telegram", client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();
        builder.Services.AddWorkflowEngine(builder.Configuration);
        // Re-registers ILeaseHolder (AddWorkflowEngine defaults to AlwaysHoldsLeaseHolder for
        // tests/Management) - last registration wins, so the engine host resolves the real one.
        builder.Services.AddSingleton<ILeaseHolder, SqlLeaseHolder>();
        builder.Services.AddScoped<IClientReferenceProvider, ClientReferenceProvider>();
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IClientReferenceProvider>>(ReferenceType.Client);
        builder.Services.AddScoped<IClientPresenceCheckProvider, ClientPresenceCheckProvider>();
        builder.Services.AddScoped<ClientPresenceParker>();
        builder.Services.AddScoped<IClientHoldStateProvider, ClientHoldStateProvider>();
        builder.Services.AddScoped<ClientHoldRecorder>();
        builder.Services.AddScoped<IDeviceCommandPublisher, DeviceCommandPublisher>();
        builder.Services.AddScoped<IToastPublisher, ToastPublisher>();
        builder.Services.AddScoped<IRunStartedPublisher, EventBusRunStartedPublisher>();
        builder.Services.AddScoped<IRunCompletedPublisher, EventBusRunCompletedPublisher>();
        builder.Services.AddSingleton<RunRecoveryService>();
        builder.Services.AddSingleton<IEngineStartupTracker>(sp => sp.GetRequiredService<RunRecoveryService>());
        // LeaderElectionService is registered first so LeadershipState is meaningful before any
        // other hosted service starts checking ILeaseHolder.IsHeldAsync. RunRecoveryService is
        // deliberately NOT a hosted service here - EngineLeadershipCoordinator calls its
        // RecoverAsync directly, only once this instance becomes leader.
        builder.Services.AddHostedService<LeaderElectionService>();
        builder.Services.AddHostedService<EngineLeadershipCoordinator>();
        builder.Services.AddHostedService<BranchExecutionPump>();
        builder.Services.AddHostedService<BookmarkScheduler>();
        builder.Services.AddHostedService<ScheduledFireTicker>();
        builder.Services.AddHostedService<ClientPresenceTicker>();
        builder.Services.AddHostedService<ClientHoldTicker>();
        builder.Services.AddHostedService<RunReaper>();
        builder.Services.AddHostedService<HistoryRetentionGc>();
        builder.Services.AddHostedService<IdempotencyKeyGc>();
        builder.Services.AddHostedService<PendingTriggerEventBacklogReaper>();
        builder.Services.AddHostedService<MetricsExporter>();
        builder.Services.AddRabbitMqEventBus(builder.Configuration);
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
        builder.Services.AddControllers();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<InboundApiKeyMiddleware>();
        app.UseMiddleware<LeaderOnlyMiddleware>();
        app.MapWbsktHealthChecks();
        app.MapControllers();

        await app.RunAsync();
    }
}
