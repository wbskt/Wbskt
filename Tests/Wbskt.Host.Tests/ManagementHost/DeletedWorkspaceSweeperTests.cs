using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Telemetry;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// The backstop behind WorkspaceDeletedEvent: it retires a workspace the auth host reports deleted only
/// when that workspace still has something live here, retires it exactly as the event's handler would,
/// counts each one, and survives the auth host being unreachable.
/// </summary>
public sealed class DeletedWorkspaceSweeperTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDeletedWorkspacesClient> _auth = new();
    private readonly Mock<IWorkspaceRetirementProvider> _provider = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IWorkflowEngineGateway> _engine = new();
    private readonly ManagementMetrics _metrics = new();
    private readonly MeterListener _listener = new();
    private readonly List<long> _retiredBySweep = [];
    private readonly List<string?> _tickOutcomes = [];

    public DeletedWorkspaceSweeperTests()
    {
        _provider.Setup(p => p.RetireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([], [], []));

        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument, _metrics.WorkspacesRetiredBySweep) || ReferenceEquals(instrument, _metrics.WorkerTicks))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (ReferenceEquals(instrument, _metrics.WorkspacesRetiredBySweep))
            {
                _retiredBySweep.Add(value);
                return;
            }

            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    _tickOutcomes.Add(tag.Value as string);
                }
            }
        });
        _listener.Start();
    }

    [Fact]
    public async Task Retires_only_the_deleted_workspaces_that_still_have_live_content()
    {
        Deleted(1, 2, 3);
        Live(1, true);
        Live(2, false);
        Live(3, true);

        int retired = await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Equal(2, retired);
        _provider.Verify(p => p.RetireAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _provider.Verify(p => p.RetireAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        _provider.Verify(p => p.RetireAsync(2, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(new[] { 1L, 1L }, _retiredBySweep);
    }

    [Fact]
    public async Task Nothing_to_repair_counts_nothing()
    {
        Deleted(5);
        Live(5, false);

        int retired = await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Equal(0, retired);
        _provider.Verify(p => p.RetireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_retiredBySweep);
    }

    [Fact]
    public async Task Asks_for_the_lookback_window_and_leaves_recent_deletes_to_their_event()
    {
        Deleted();

        await Sweeper(new WorkspaceReconciliationOptions { LookbackDays = 10, GracePeriod = TimeSpan.FromHours(2) }).SweepAsync(CancellationToken.None);

        _auth.Verify(a => a.GetDeletedBetweenAsync(Now.UtcDateTime.AddDays(-10), Now.UtcDateTime.AddHours(-2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_workspace_it_retires_is_announced_as_the_delete_event_would_announce_it()
    {
        Deleted(7);
        Live(7, true);
        var client = new RetiredClient(41, Guid.NewGuid(), 9, Guid.NewGuid());
        _provider.Setup(p => p.RetireAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([client], [501L], [61, 62]));

        await Sweeper().SweepAsync(CancellationToken.None);

        _engine.Verify(e => e.CancelRunAsync(501L, "Workspace deleted.", It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.PublishAsync(
            It.Is<WorkflowDefinitionChangedEvent>(e => e.WorkspaceId == 7 && e.DefinitionIds.SequenceEqual(new[] { 61, 62 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_auth_host_that_cannot_answer_fails_the_tick_and_the_next_tick_sweeps()
    {
        _auth.SetupSequence(a => a.GetDeletedBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("auth down"))
            .ReturnsAsync([4]);
        Live(4, true);
        var sweeper = Sweeper();

        await Assert.ThrowsAsync<HttpRequestException>(() => sweeper.TickAsync(CancellationToken.None));
        _provider.Verify(p => p.RetireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        await sweeper.TickAsync(CancellationToken.None);

        _provider.Verify(p => p.RetireAsync(4, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "failed", "ok" }, _tickOutcomes);
        Assert.Equal(new[] { 1L }, _retiredBySweep);
    }

    [Fact]
    public async Task A_workspace_that_cannot_be_retired_does_not_hold_up_the_rest()
    {
        Deleted(1, 2);
        Live(1, true);
        Live(2, true);
        _provider.Setup(p => p.RetireAsync(1, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sweeper().SweepAsync(CancellationToken.None));

        _provider.Verify(p => p.RetireAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { 1L }, _retiredBySweep);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }

    private void Deleted(params int[] workspaceIds) =>
        _auth.Setup(a => a.GetDeletedBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceIds);

    private void Live(int workspaceId, bool live) =>
        _provider.Setup(p => p.HasLiveContentAsync(workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(live);

    private DeletedWorkspaceSweeper Sweeper(WorkspaceReconciliationOptions? options = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(_auth.Object)
            .AddSingleton(_provider.Object)
            .AddSingleton(new WorkspaceRetirer(_provider.Object, _bus.Object, _engine.Object, Mock.Of<IClientTokenCutoffs>(), NullLogger<WorkspaceRetirer>.Instance))
            .AddSingleton<IWorkerTickMetrics>(_metrics)
            .BuildServiceProvider();

        return new DeletedWorkspaceSweeper(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            Options.Create(options ?? new WorkspaceReconciliationOptions()),
            _metrics,
            NullLogger<DeletedWorkspaceSweeper>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
