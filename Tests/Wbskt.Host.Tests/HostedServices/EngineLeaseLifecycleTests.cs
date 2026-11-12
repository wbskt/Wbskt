using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

/// <summary>
/// The engine-leader lease across a host's life: a leader that stops, for whatever reason, hands the
/// lease back so the standby does not wait out the TTL.
/// </summary>
public sealed class EngineLeaseLifecycleTests
{
    private const string HostId = "host-under-test";

    [Fact]
    public async Task A_leader_releases_the_lease_when_it_stops()
    {
        var lease = new RecordingLeaseProvider(acquire: true);
        var state = new LeadershipState();
        var service = CreateElectionService(lease, state);

        await service.StartAsync(CancellationToken.None);
        await state.WhenLeaderAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([("engine-leader", HostId)], lease.Released);
    }

    [Fact]
    public async Task A_standby_that_never_led_releases_nothing()
    {
        var lease = new RecordingLeaseProvider(acquire: false);
        var service = CreateElectionService(lease, new LeadershipState());

        await service.StartAsync(CancellationToken.None);
        await lease.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(lease.Released);
    }

    [Fact]
    public async Task A_failed_release_does_not_fail_the_shutdown()
    {
        var lease = new RecordingLeaseProvider(acquire: true, failRelease: true);
        var state = new LeadershipState();
        var service = CreateElectionService(lease, state);

        await service.StartAsync(CancellationToken.None);
        await state.WhenLeaderAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Exception? thrown = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        Assert.Null(thrown);
        Assert.Single(lease.Released);
    }

    [Fact]
    public async Task A_leader_whose_recovery_fails_logs_critical_stops_and_still_releases_the_lease()
    {
        var events = new ConcurrentQueue<string>();
        var lease = new RecordingLeaseProvider(acquire: true, events: events);
        var logs = new RecordingLoggerProvider();

        var busHandle = new Mock<BusHandle>();
        busHandle.Setup(h => h.StopAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Enqueue("bus-stopped"))
            .Returns(Task.CompletedTask);
        var bus = new Mock<IBusControl>();
        bus.Setup(b => b.StartAsync(It.IsAny<CancellationToken>())).ReturnsAsync(busHandle.Object);

        var branches = new Mock<IBranchProvider>();
        branches.Setup(b => b.GetWokenAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        branches.Setup(b => b.GetRunningBranchesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));
        var claims = new Mock<IIdempotencyKeyProvider>();
        claims.Setup(c => c.FailAbandonedClaimsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        using IHost host = new HostBuilder()
            .ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs))
            .ConfigureServices(services =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<LeadershipState>();
                services.AddSingleton<IHostIdentity>(new FixedHostIdentity());
                services.AddSingleton<ILeaseProvider>(lease);
                services.AddSingleton(bus.Object);
                services.AddSingleton(branches.Object);
                services.AddSingleton(claims.Object);
                services.AddSingleton(Mock.Of<IRunDispatcher>());
                services.AddSingleton<RunRecoveryService>();
                services.AddSingleton(WorkerTestServices.Options(new WorkflowEngineOptions { LeaderElectionRenewInterval = TimeSpan.FromMilliseconds(50) }));
                // The engine's order (see Program.cs): election first, so it is stopped last.
                services.AddHostedService<LeaderElectionService>();
                services.AddHostedService<EngineLeadershipCoordinator>();
            })
            .Build();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        // What RunAsync does in the engine: wait for the host to ask to stop, then stop it.
        await host.WaitForShutdownAsync(timeout.Token);

        Assert.False(timeout.IsCancellationRequested, "the failed recovery should have stopped the host by itself");
        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Critical
            && entry.Category.EndsWith(nameof(EngineLeadershipCoordinator), StringComparison.Ordinal)
            && entry.Message.Contains("database unreachable", StringComparison.Ordinal)
            && entry.Exception is InvalidOperationException);
        Assert.Equal([("engine-leader", HostId)], lease.Released);
        Assert.Equal(["bus-stopped", "lease-released"], events.Where(e => e is "bus-stopped" or "lease-released"));
    }

    [Fact]
    public async Task A_new_leader_reads_the_woken_branches_and_releases_abandoned_claims_before_it_starts_the_bus()
    {
        // wbskt/Wbskt#768: once the bus runs and the gate is open, the scheduler and consumers wake branches
        // of their own, which look the same as the ones the previous leader woke and lost; read then, they
        // would be run twice. #770: the same holds for the event claims a stopped engine left Pending.
        var steps = new ConcurrentQueue<string>();
        var state = new LeadershipState();
        var lease = new RecordingLeaseProvider(acquire: true, events: new ConcurrentQueue<string>());
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = new Mock<IBusControl>();
        bus.Setup(b => b.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => steps.Enqueue("bus-started"))
            .ReturnsAsync(Mock.Of<BusHandle>());

        var woken = new BranchRow
        {
            Id = 81, RefId = Guid.NewGuid(), RunId = 181, ParentBranchId = null, ForkCohortId = null,
            NodeId = Guid.NewGuid(), Status = "Waiting", PendingTakePort = null, LocalJson = "{}", LastOutputJson = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, RowVersion = [1]
        };
        var branches = new Mock<IBranchProvider>();
        branches.Setup(b => b.GetWokenAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => steps.Enqueue(state.BusStarted ? "woken-read-after-gate" : "woken-read"))
            .ReturnsAsync([woken]);
        branches.Setup(b => b.GetRunningBranchesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var claims = new Mock<IIdempotencyKeyProvider>();
        claims.Setup(c => c.FailAbandonedClaimsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => steps.Enqueue(state.BusStarted ? "claims-released-after-gate" : "claims-released"))
            .ReturnsAsync(2);

        var dispatcher = new Mock<IRunDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<BranchExecutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BranchExecutionRequest, CancellationToken>((request, _) =>
            {
                steps.Enqueue($"dispatch-{request.BranchId}");
                recovered.TrySetResult();
            })
            .Returns(ValueTask.CompletedTask);

        using IHost host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton(state);
                services.AddSingleton<IHostIdentity>(new FixedHostIdentity());
                services.AddSingleton<ILeaseProvider>(lease);
                services.AddSingleton(bus.Object);
                services.AddSingleton(branches.Object);
                services.AddSingleton(claims.Object);
                services.AddSingleton(dispatcher.Object);
                services.AddSingleton<RunRecoveryService>();
                services.AddSingleton(WorkerTestServices.Options(new WorkflowEngineOptions { LeaderElectionRenewInterval = TimeSpan.FromMilliseconds(50) }));
                services.AddHostedService<LeaderElectionService>();
                services.AddHostedService<EngineLeadershipCoordinator>();
            })
            .Build();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        await recovered.Task.WaitAsync(timeout.Token);
        await host.StopAsync(timeout.Token);

        Assert.Equal(["woken-read", "claims-released", "bus-started", "dispatch-81"], steps);
    }

    private static LeaderElectionService CreateElectionService(ILeaseProvider lease, LeadershipState state) =>
        new(
            WorkerTestServices.Scopes(lease),
            new FixedHostIdentity(),
            state,
            TimeProvider.System,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<LeaderElectionService>.Instance,
            WorkerTestServices.Options(new WorkflowEngineOptions { LeaderElectionRenewInterval = TimeSpan.FromMilliseconds(50) }));

    private sealed class FixedHostIdentity : IHostIdentity
    {
        public string HostId => EngineLeaseLifecycleTests.HostId;
    }

    private sealed class RecordingLeaseProvider(bool acquire, bool failRelease = false, ConcurrentQueue<string>? events = null) : ILeaseProvider
    {
        private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<(string LeaseName, string HolderId)> ReleasedQueue { get; } = new();

        public IReadOnlyList<(string LeaseName, string HolderId)> Released => ReleasedQueue.ToList();

        public Task FirstAttempt => _firstAttempt.Task;

        public Task<bool> TryAcquireAsync(string leaseName, string holderId, TimeSpan ttl, CancellationToken ct)
        {
            _firstAttempt.TrySetResult();
            return Task.FromResult(acquire);
        }

        public Task ReleaseAsync(string leaseName, string holderId, CancellationToken ct)
        {
            ReleasedQueue.Enqueue((leaseName, holderId));
            events?.Enqueue("lease-released");
            return failRelease ? Task.FromException(new InvalidOperationException("release failed")) : Task.CompletedTask;
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries.ToList();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
