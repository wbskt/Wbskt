using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.HostedServices;

/// <summary>
/// What a deploy does to a step that is running: it finishes while the bus is still up, so the command
/// or notification it sends goes out instead of failing on a stopped bus.
/// </summary>
public sealed class EngineShutdownOrderTests
{
    [Fact]
    public void The_pump_stops_before_the_bus_and_the_bus_before_the_lease()
    {
        var services = new ServiceCollection();
        services.AddEngineHostServices(new ConfigurationBuilder().Build());

        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        // Hosted services stop in reverse order of registration.
        Assert.True(hosted.IndexOf(typeof(LeaderElectionService)) < hosted.IndexOf(typeof(EngineLeadershipCoordinator)));
        Assert.True(hosted.IndexOf(typeof(EngineLeadershipCoordinator)) < hosted.IndexOf(typeof(BranchExecutionPump)));
    }

    [Fact]
    public async Task A_step_running_when_the_host_stops_publishes_on_a_live_bus()
    {
        var bus = new BusStandIn();
        var loop = new PublishingBranchLoop(bus);
        var later = new ServiceStoppedBeforeThePump(loop);
        using IHost host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new WorkflowEngineOptions { BranchDrainGrace = TimeSpan.FromSeconds(5) }));
                services.AddSingleton<ChannelRunDispatcher>();
                services.AddScoped<IBranchLoop>(_ => loop);

                // The order the engine host registers them in: the bus's owner, then the pump, then the
                // services stopped before the pump.
                services.AddHostedService(_ => bus);
                services.AddHostedService<BranchExecutionPump>();
                services.AddHostedService(_ => later);
            })
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(cts.Token);
        var dispatcher = host.Services.GetRequiredService<ChannelRunDispatcher>();
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted), cts.Token);
        await loop.StepStarted.Task.WaitAsync(cts.Token);

        await host.StopAsync(cts.Token);

        // Told to start no new node as soon as the host began stopping, before any service stopped.
        Assert.True(later.DrainSeenWhenStopped);
        Assert.True(loop.Published);
        Assert.True(loop.BusWasUpWhenPublished);
    }

    private sealed class BusStandIn : IHostedService
    {
        public bool IsUp { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsUp = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsUp = false;
            return Task.CompletedTask;
        }
    }

    private sealed class ServiceStoppedBeforeThePump(PublishingBranchLoop loop) : IHostedService
    {
        public bool DrainSeenWhenStopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            DrainSeenWhenStopped = loop.DrainSeen.Task.IsCompleted;
            return Task.CompletedTask;
        }
    }

    // One step that, once told the host is draining, finishes by publishing, as a "send to client" does.
    private sealed class PublishingBranchLoop(BusStandIn bus) : IBranchLoop
    {
        public TaskCompletionSource StepStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DrainSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Published { get; private set; }
        public bool BusWasUpWhenPublished { get; private set; }

        public async Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken drainToken, CancellationToken ct)
        {
            using CancellationTokenRegistration drain = drainToken.Register(() => DrainSeen.TrySetResult());
            StepStarted.TrySetResult();
            await DrainSeen.Task.WaitAsync(ct);

            // Let the host carry on stopping the services before the pump first.
            await Task.Delay(50, ct);
            BusWasUpWhenPublished = bus.IsUp;
            Published = true;
        }
    }
}
