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

        Assert.Equal(2, provider.Calls.Count);
        Assert.All(provider.Calls, call => Assert.Equal((new DateOnly(2026, 5, 26), CreditUsagePoster.BatchSize), call));
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
    public async Task A_post_that_carries_a_tenant_across_its_monthly_credits_warns_once_for_each_line_crossed()
    {
        // Tenant 1's two workspaces post 300 together, taking it from 700 to 1000 of 1000: 80% and 100%,
        // named for the workspace that posted most. Tenant 2 stays under 80%; tenant 3 was past 80% already.
        var provider = new RecordingCreditUsageProvider([
            [
                new WorkspaceCreditPost(11, 1, 100m, 100m, null, TenantId: 1, CreditsPerMonth: 1000m, TenantSpentThisMonth: 1000m),
                new WorkspaceCreditPost(12, 1, 200m, 900m, null, TenantId: 1, CreditsPerMonth: 1000m, TenantSpentThisMonth: 1000m),
                new WorkspaceCreditPost(21, 1, 10m, 10m, null, TenantId: 2, CreditsPerMonth: 1000m, TenantSpentThisMonth: 790m),
                new WorkspaceCreditPost(31, 1, 10m, 10m, null, TenantId: 3, CreditsPerMonth: 1000m, TenantSpentThisMonth: 850m),
                new WorkspaceCreditPost(41, 1, 10m, 10m, null)
            ]
        ]);
        var bus = new RecordingEventBus();

        await Poster(provider, bus, isLeader: true).TickAsync(CancellationToken.None);

        Assert.Equal(
            [(1, 12, 80, 1000m), (1, 12, 100, 1000m)],
            bus.Published.OfType<TenantCreditAllowanceReachedEvent>().Select(e => (e.TenantId, e.WorkspaceId, e.Percent, e.CreditsSpent)).Order());
    }

    [Fact]
    public async Task A_warning_the_broker_refuses_is_tried_again_since_the_crossing_cannot_be_seen_later()
    {
        var provider = new RecordingCreditUsageProvider([
            [new WorkspaceCreditPost(11, 1, 300m, 300m, null, TenantId: 1, CreditsPerMonth: 1000m, TenantSpentThisMonth: 850m)]
        ]);
        var bus = new RecordingEventBus(failures: 1);

        await Poster(provider, bus, isLeader: true).TickAsync(CancellationToken.None);

        Assert.Equal([80], bus.Published.OfType<TenantCreditAllowanceReachedEvent>().Select(e => e.Percent));
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

    private sealed class RecordingEventBus(int failures = 0) : IEventBus
    {
        private int _failures = failures;

        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            if (_failures-- > 0)
            {
                throw new InvalidOperationException("The broker is down.");
            }

            Published.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCreditUsageProvider(IReadOnlyCollection<WorkspaceCreditPost>[] posts) : ICreditUsageProvider
    {
        private readonly Queue<IReadOnlyCollection<WorkspaceCreditPost>> _posts = new(posts);

        public List<(DateOnly Day, int BatchSize)> Calls { get; } = [];

        public Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, int batchSize, CancellationToken ct)
        {
            Calls.Add((day, batchSize));
            return Task.FromResult(_posts.Count > 0 ? _posts.Dequeue() : []);
        }
    }
}
