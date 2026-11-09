using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class MessageAllowanceMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly long Free = PlanCatalog.BuiltIn[PlanCode.Free].MessagesPerMonth;
    private static readonly long Maker = PlanCatalog.BuiltIn[PlanCode.Maker].MessagesPerMonth;
    private static readonly long Pro = PlanCatalog.BuiltIn[PlanCode.Pro].MessagesPerMonth;
    private static readonly long Business = PlanCatalog.BuiltIn[PlanCode.Business].MessagesPerMonth;

    private readonly Mock<IMessageAllowanceProvider> _allowance = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly List<IEvent> _events = [];
    private readonly Mock<IEventBus> _bus = new();
    private MessageThrottleSnapshot? _snapshot;

    public MessageAllowanceMonitorTests()
    {
        _publisher.Setup(x => x.Publish(It.IsAny<MessageThrottleSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<MessageThrottleSnapshot, CancellationToken>((s, _) => _snapshot = s)
            .Returns(Task.CompletedTask);
        _bus.Setup(x => x.PublishAsync(It.IsAny<WorkspaceMessageLimitReachedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMessageLimitReachedEvent, CancellationToken>((e, _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        _bus.Setup(x => x.PublishAsync(It.IsAny<WorkspaceMessageLimitApproachingEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMessageLimitApproachingEvent, CancellationToken>((e, _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        _bus.Setup(x => x.PublishAsync(It.IsAny<WorkspaceMessagesThrottledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMessagesThrottledEvent, CancellationToken>((e, _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        _allowance.Setup(x => x.RecordNoticeAsync(It.IsAny<IReadOnlyCollection<int>>(), November, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, DateOnly _, int _, CancellationToken _) => ids);
    }

    [Fact]
    public async Task Free_is_throttled_at_its_allowance_maker_at_twice_it_and_each_crossing_is_announced()
    {
        Accounts(
            new WorkspaceMessageAccount(1, PlanCode.Free, null, Free),
            new WorkspaceMessageAccount(2, PlanCode.Free, null, Free - 1),
            new WorkspaceMessageAccount(3, PlanCode.Maker, null, Maker * 3 / 2),
            new WorkspaceMessageAccount(4, PlanCode.Maker, null, Maker * 2),
            new WorkspaceMessageAccount(5, PlanCode.Maker, null, Maker * 7 / 10));

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.Equal([1, 4], _snapshot!.WorkspaceIds);
        Assert.Equal(Now.UtcDateTime, _snapshot.TakenAtUtc);
        Assert.Equal(
            new IEvent[]
            {
                new WorkspaceMessageLimitApproachingEvent(2, Free, Free - 1),
                new WorkspaceMessageLimitReachedEvent(1, Free, Free, true),
                new WorkspaceMessageLimitReachedEvent(3, Maker, Maker * 3 / 2, false),
                new WorkspaceMessageLimitReachedEvent(4, Maker, Maker * 2, true),
                new WorkspaceMessagesThrottledEvent(4, Maker, Maker * 2, Maker * 2)
            }.Select(Undated),
            _events.Select(Undated));
    }

    [Fact]
    public async Task Business_is_never_throttled_and_a_changed_multiple_moves_the_paid_throttle()
    {
        Accounts(
            new WorkspaceMessageAccount(1, PlanCode.Business, null, Business * 10),
            new WorkspaceMessageAccount(2, PlanCode.Pro, null, Pro * 3 / 2));

        await Monitor(paidMultiple: 1.5m).CheckAsync(CancellationToken.None);

        Assert.Equal([2], _snapshot!.WorkspaceIds);
        Assert.Contains(Undated(new WorkspaceMessagesThrottledEvent(2, Pro, Pro * 3 / 2, Pro * 3 / 2)), _events.Select(Undated));
        _allowance.Verify(x => x.RecordNoticeAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 2 })), November, 150, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_tenants_negotiated_allowance_replaces_the_plans()
    {
        Accounts(new WorkspaceMessageAccount(1, PlanCode.Free, new PlanLimitOverrides { MessagesPerMonth = Free * 2 }, Free));

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.Empty(_snapshot!.WorkspaceIds);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task A_workspace_already_announced_this_month_is_not_announced_again_but_stays_throttled()
    {
        Accounts(new WorkspaceMessageAccount(1, PlanCode.Free, null, Free + 10));
        _allowance.Setup(x => x.RecordNoticeAsync(It.IsAny<IReadOnlyCollection<int>>(), November, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.Equal([1], _snapshot!.WorkspaceIds);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task A_notice_the_broker_refuses_is_given_back_so_the_next_check_raises_it_and_the_rest_still_go()
    {
        Accounts(
            new WorkspaceMessageAccount(1, PlanCode.Free, null, Free),
            new WorkspaceMessageAccount(2, PlanCode.Free, null, Free));
        _bus.Setup(x => x.PublishAsync(It.Is<WorkspaceMessageLimitReachedEvent>(e => e.WorkspaceId == 1), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The broker is down."));

        await Monitor().CheckAsync(CancellationToken.None);

        _allowance.Verify(x => x.ReleaseNoticeAsync(1, November, 100, It.IsAny<CancellationToken>()), Times.Once);
        _allowance.Verify(x => x.ReleaseNoticeAsync(2, It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal([2], _events.OfType<WorkspaceMessageLimitReachedEvent>().Select(e => e.WorkspaceId));
    }

    [Fact]
    public async Task With_nobody_over_the_empty_list_is_still_sent_so_the_socket_hosts_let_go()
    {
        Accounts();

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.NotNull(_snapshot);
        Assert.Empty(_snapshot.WorkspaceIds);
    }

    private void Accounts(params WorkspaceMessageAccount[] accounts) =>
        _allowance.Setup(x => x.GetAccountsAsync(November, It.IsAny<CancellationToken>())).ReturnsAsync(accounts);

    private MessageAllowanceMonitor Monitor(decimal paidMultiple = MessageAllowance.DefaultPaidThrottleMultiple) =>
        new(_allowance.Object, new PlanCatalog(Options.Create(new PlanCatalogOptions())), _publisher.Object, _bus.Object, new FixedClock(),
            Options.Create(new BillingOptions { PaidMessageThrottleMultiple = paidMultiple }), NullLogger<MessageAllowanceMonitor>.Instance);

    private static IEvent Undated(IEvent e) => e switch
    {
        WorkspaceMessageLimitApproachingEvent a => a with { CreatedAtUtc = default },
        WorkspaceMessageLimitReachedEvent r => r with { CreatedAtUtc = default },
        WorkspaceMessagesThrottledEvent t => t with { CreatedAtUtc = default },
        _ => e
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
