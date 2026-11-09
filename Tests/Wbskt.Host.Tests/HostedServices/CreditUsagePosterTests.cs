using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class CreditUsagePosterTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tick_posts_in_batches_on_todays_date_until_caught_up()
    {
        var provider = new RecordingCreditUsageProvider([
            [new WorkspaceCreditPost(1, CreditUsagePoster.BatchSize - 1, 5m, 5m, null), new WorkspaceCreditPost(2, 1, 1m, 1m, null)],
            [new WorkspaceCreditPost(1, 3, 1m, 6m, null)]
        ]);
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true);

        await poster.TickAsync(CancellationToken.None);

        // Runs that ended within the last hour (the default window) are still read.
        Assert.Equal(2, provider.Calls.Count);
        Assert.All(provider.Calls, call => Assert.Equal((new DateOnly(2026, 5, 26), Now.AddHours(-1), CreditUsagePoster.BatchSize), call));
    }

    [Fact]
    public async Task A_tick_stops_after_its_batch_limit_while_runs_keep_charging()
    {
        WorkspaceCreditPost[] full = [new WorkspaceCreditPost(1, CreditUsagePoster.BatchSize, 1m, 1m, null)];
        var provider = new RecordingCreditUsageProvider(Enumerable.Repeat(full, CreditUsagePoster.MaxBatchesPerTick + 5).ToArray());
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true);

        await poster.TickAsync(CancellationToken.None);

        Assert.Equal(CreditUsagePoster.MaxBatchesPerTick, provider.Calls.Count);
    }

    [Fact]
    public async Task A_post_that_carries_a_workspace_across_its_cap_thresholds_warns_once_for_each()
    {
        var provider = new RecordingCreditUsageProvider([
            [
                new WorkspaceCreditPost(7, 2, 30m, 100m, 100m),
                new WorkspaceCreditPost(8, 1, 1m, 50m, 100m),
                new WorkspaceCreditPost(9, 1, 10m, 85m, 100m),
                new WorkspaceCreditPost(10, 1, 500m, 500m, null)
            ]
        ]);
        var bus = new RecordingEventBus();
        CreditUsagePoster poster = Poster(provider, bus, isLeader: true);

        await poster.TickAsync(CancellationToken.None);

        Assert.Equal(
            [(7, 80), (7, 100), (9, 80)],
            bus.Published.OfType<WorkspaceCreditCapReachedEvent>().Select(e => (e.WorkspaceId, e.Percent)).Order());
    }

    [Fact]
    public async Task A_standby_posts_nothing()
    {
        var provider = new RecordingCreditUsageProvider([]);
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: false);

        await poster.TickAsync(CancellationToken.None);

        Assert.Empty(provider.Calls);
    }

    private static CreditUsagePoster Poster(RecordingCreditUsageProvider provider, RecordingEventBus bus, bool isLeader) =>
        new(new FixedClock(), new FixedLeadershipGate(isLeader), WorkerTestServices.Scopes(provider, bus), NullLogger<CreditUsagePoster>.Instance, WorkerTestServices.Options());

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public bool IsActive => isActive;
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCreditUsageProvider(IReadOnlyCollection<WorkspaceCreditPost>[] posts) : ICreditUsageProvider
    {
        private readonly Queue<IReadOnlyCollection<WorkspaceCreditPost>> _posts = new(posts);

        public List<(DateOnly Day, DateTime EndedSince, int BatchSize)> Calls { get; } = [];

        public Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, DateTime endedSinceUtc, int batchSize, CancellationToken ct)
        {
            Calls.Add((day, endedSinceUtc, batchSize));
            return Task.FromResult(_posts.Count > 0 ? _posts.Dequeue() : []);
        }
    }
}
