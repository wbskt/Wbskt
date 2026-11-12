using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
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
            [new WorkspaceCreditPost(1, CreditUsagePoster.BatchSize - 1, 5m), new WorkspaceCreditPost(2, 1, 1m)],
            [new WorkspaceCreditPost(1, 3, 1m)]
        ]);
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true);

        await poster.TickAsync(CancellationToken.None);

        Assert.Equal(2, provider.Calls.Count);
        Assert.All(provider.Calls, call => Assert.Equal((new DateOnly(2026, 5, 26), CreditUsagePoster.BatchSize), call));
    }

    [Fact]
    public async Task A_tick_stops_after_its_batch_limit_while_runs_keep_charging()
    {
        WorkspaceCreditPost[] full = [new WorkspaceCreditPost(1, CreditUsagePoster.BatchSize, 1m)];
        var provider = new RecordingCreditUsageProvider(Enumerable.Repeat(full, CreditUsagePoster.MaxBatchesPerTick + 5).ToArray());
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true);

        await poster.TickAsync(CancellationToken.None);

        Assert.Equal(CreditUsagePoster.MaxBatchesPerTick, provider.Calls.Count);
    }

    [Fact]
    public async Task Each_threshold_owed_is_announced_and_marked_lowest_first()
    {
        var provider = new RecordingCreditUsageProvider([], [
            new CreditNotice(CreditNoticeKind.Cap, 7, null, 100m, 100m, 100, 0),
            new CreditNotice(CreditNoticeKind.Cap, 9, null, 100m, 85m, 80, 0),
            new CreditNotice(CreditNoticeKind.Allowance, 12, 1, 1000m, 1000m, 100, 80)
        ]);
        var bus = new RecordingEventBus();

        await Poster(provider, bus, isLeader: true).TickAsync(CancellationToken.None);

        Assert.Equal(
            [(7, 80), (7, 100), (9, 80)],
            bus.Published.OfType<WorkspaceCreditCapReachedEvent>().Select(e => (e.WorkspaceId, e.Percent)));
        Assert.Equal(
            [(1, 12, 100, 1000m)],
            bus.Published.OfType<TenantCreditAllowanceReachedEvent>().Select(e => (e.TenantId, e.WorkspaceId, e.Percent, e.CreditsSpent)));
        Assert.Equal([(CreditNoticeKind.Cap, 7, 80), (CreditNoticeKind.Cap, 7, 100), (CreditNoticeKind.Cap, 9, 80), (CreditNoticeKind.Allowance, 1, 100)], provider.Marked);
        Assert.All(provider.MarkedMonths, month => Assert.Equal(new DateOnly(2026, 5, 1), month));
    }

    [Fact]
    public async Task A_notice_the_broker_refuses_stays_unmarked_and_is_tried_on_the_next_tick()
    {
        var provider = new RecordingCreditUsageProvider([], [new CreditNotice(CreditNoticeKind.Allowance, 11, 1, 1000m, 850m, 80, 0)]);
        var bus = new RecordingEventBus(failures: 1);
        CreditUsagePoster poster = Poster(provider, bus, isLeader: true);

        await poster.TickAsync(CancellationToken.None);
        bool markedAfterRefusal = provider.Marked.Count > 0;
        await poster.TickAsync(CancellationToken.None);

        Assert.False(markedAfterRefusal);
        Assert.Equal([80], bus.Published.OfType<TenantCreditAllowanceReachedEvent>().Select(e => e.Percent));
        Assert.Equal([(CreditNoticeKind.Allowance, 1, 80)], provider.Marked);
    }

    [Fact]
    public async Task Notices_are_looked_for_a_minute_after_a_post_and_every_five_minutes_without_one()
    {
        WorkspaceCreditPost[] post = [new WorkspaceCreditPost(1, 1, 1m)];
        var provider = new RecordingCreditUsageProvider([[], post, [], post, [], []]);
        var clock = new SettableClock();
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true, clock);
        var checks = new List<int>();

        foreach (TimeSpan at in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(70), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6.2) })
        {
            clock.Now = Now + at;
            await poster.TickAsync(CancellationToken.None);
            checks.Add(provider.NoticeChecks);
        }

        // The first tick checks; a post 30 s later does not, one 70 s after the check does; a quiet tick
        // checks again five minutes after the last check.
        Assert.Equal([1, 1, 1, 2, 2, 3], checks);
    }

    [Fact]
    public async Task A_notice_whose_mark_fails_is_looked_for_again_on_the_next_tick()
    {
        var provider = new RecordingCreditUsageProvider([], [new CreditNotice(CreditNoticeKind.Cap, 7, null, 100m, 90m, 80, 0)], markFailures: 1);
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => poster.TickAsync(CancellationToken.None));
        await poster.TickAsync(CancellationToken.None);

        Assert.Equal(2, provider.NoticeChecks);
        Assert.Equal([(CreditNoticeKind.Cap, 7, 80)], provider.Marked);
    }

    [Fact]
    public async Task A_standby_posts_nothing()
    {
        var provider = new RecordingCreditUsageProvider([]);
        CreditUsagePoster poster = Poster(provider, new RecordingEventBus(), isLeader: false);

        await poster.TickAsync(CancellationToken.None);

        Assert.Empty(provider.Calls);
    }

    private static CreditUsagePoster Poster(RecordingCreditUsageProvider provider, RecordingEventBus bus, bool isLeader, TimeProvider? clock = null) =>
        new(clock ?? new SettableClock(), new FixedLeadershipGate(isLeader), WorkerTestServices.Scopes(provider, bus), NullLogger<CreditUsagePoster>.Instance, WorkerTestServices.Options());

    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = CreditUsagePosterTests.Now;

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

    private sealed class RecordingCreditUsageProvider(IReadOnlyCollection<WorkspaceCreditPost>[] posts, IReadOnlyCollection<CreditNotice>? due = null, int markFailures = 0) : ICreditUsageProvider
    {
        private readonly Queue<IReadOnlyCollection<WorkspaceCreditPost>> _posts = new(posts);
        private int _markFailures = markFailures;

        public List<(DateOnly Day, int BatchSize)> Calls { get; } = [];

        public List<(CreditNoticeKind Kind, int Id, int Percent)> Marked { get; } = [];

        public List<DateOnly> MarkedMonths { get; } = [];

        public int NoticeChecks { get; private set; }

        public Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, int batchSize, CancellationToken ct)
        {
            Calls.Add((day, batchSize));
            return Task.FromResult(_posts.Count > 0 ? _posts.Dequeue() : []);
        }

        public Task<IReadOnlyCollection<CreditNotice>> GetDueNoticesAsync(DateOnly monthStart, CancellationToken ct)
        {
            NoticeChecks++;
            IReadOnlyCollection<CreditNotice> owed = (due ?? [])
                .Select(n => n with { Notified = Marked.Where(m => m.Kind == n.Kind && m.Id == IdOf(n)).Select(m => m.Percent).DefaultIfEmpty(n.Notified).Max() })
                .Where(n => n.Level > n.Notified)
                .ToArray();
            return Task.FromResult(owed);
        }

        public Task MarkNotifiedAsync(CreditNotice notice, DateOnly monthStart, int percent, CancellationToken ct)
        {
            if (_markFailures-- > 0)
            {
                throw new InvalidOperationException("The database is down.");
            }

            Marked.Add((notice.Kind, IdOf(notice), percent));
            MarkedMonths.Add(monthStart);
            return Task.CompletedTask;
        }

        // A cap is marked on its workspace, an allowance on its tenant.
        private static int IdOf(CreditNotice notice) => notice.Kind == CreditNoticeKind.Cap ? notice.WorkspaceId : notice.TenantId ?? 0;
    }
}
