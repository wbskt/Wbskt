using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class GracefulShutdownIntegrationTests
{
    [Fact]
    public async Task Host_with_phase10_services_starts_and_stops_cleanly()
    {
        using IHost host = new HostBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>());
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddWorkflowRuntime(context.Configuration);
                services.AddSingleton<ILeaseHolder, AlwaysHoldsLeaseHolder>();
                services.AddSingleton<WorkflowMetrics>();

                services.AddScoped(_ => CreateBookmarkProvider());
                services.AddScoped(_ => CreateBranchProvider());
                services.AddScoped(_ => CreateRunProvider());
                services.AddScoped(_ => CreateHistoryEventProvider());
                services.AddScoped(_ => CreatePendingTriggerEventProvider());
                services.AddScoped(_ => CreateScheduledFireProvider());
                services.AddScoped(_ => CreateRunCountersProvider());
                services.AddScoped(_ => CreateInboundHub());
                services.AddScoped(_ => CreateRunCancellationService());

                services.AddHostedService<BranchExecutionPump>();
                services.AddHostedService<BookmarkScheduler>();
                services.AddHostedService<ScheduledFireTicker>();
                services.AddHostedService<RunReaper>();
                services.AddHostedService<HistoryRetentionGc>();
                services.AddHostedService<PendingTriggerEventBacklogReaper>();
                services.AddHostedService<RunRecoveryService>();
                services.AddHostedService<MetricsExporter>();
            })
            .Build();

        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(startCts.Token);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(stopCts.Token);
    }

    private static IBookmarkProvider CreateBookmarkProvider()
    {
        var mock = new Mock<IBookmarkProvider>(MockBehavior.Strict);
        mock.Setup(x => x.LeaseDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BookmarkRow>());
        mock.Setup(x => x.DeleteOrphansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(x => x.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        return mock.Object;
    }

    private static IBranchProvider CreateBranchProvider()
    {
        var mock = new Mock<IBranchProvider>(MockBehavior.Strict);
        mock.Setup(x => x.GetRunningBranchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BranchRow>());
        return mock.Object;
    }

    private static IRunProvider CreateRunProvider()
    {
        var mock = new Mock<IRunProvider>(MockBehavior.Strict);
        mock.Setup(x => x.GetStuckRunsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RunRow>());
        mock.Setup(x => x.CountByStatusAsync("Running", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        return mock.Object;
    }

    private static IHistoryEventProvider CreateHistoryEventProvider()
    {
        var mock = new Mock<IHistoryEventProvider>(MockBehavior.Strict);
        mock.Setup(x => x.DeleteForRetiredRunsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        return mock.Object;
    }

    private static IPendingTriggerEventProvider CreatePendingTriggerEventProvider()
    {
        var mock = new Mock<IPendingTriggerEventProvider>(MockBehavior.Strict);
        mock.Setup(x => x.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(x => x.CountAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        return mock.Object;
    }

    private static IScheduledFireProvider CreateScheduledFireProvider()
    {
        var mock = new Mock<IScheduledFireProvider>(MockBehavior.Strict);
        mock.Setup(x => x.LeaseDueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduledFireRow>());
        return mock.Object;
    }

    private static IRunCountersProvider CreateRunCountersProvider()
    {
        var mock = new Mock<IRunCountersProvider>(MockBehavior.Strict);
        mock.Setup(x => x.SumActiveBranchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        return mock.Object;
    }

    private static IInboundHub CreateInboundHub()
    {
        var mock = new Mock<IInboundHub>(MockBehavior.Strict);
        return mock.Object;
    }

    private static IRunCancellationService CreateRunCancellationService()
    {
        var mock = new Mock<IRunCancellationService>(MockBehavior.Strict);
        return mock.Object;
    }
}
