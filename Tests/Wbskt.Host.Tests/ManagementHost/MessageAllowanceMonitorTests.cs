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
        _allowance.Setup(x => x.RecordLimitReachedAsync(It.IsAny<IReadOnlyCollection<int>>(), November, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, DateOnly _, CancellationToken _) => ids);
    }

    [Fact]
    public async Task Only_free_accounts_over_their_messages_are_throttled_but_every_account_over_is_announced()
    {
        Accounts(
            new WorkspaceMessageAccount(1, PlanCode.Free, null, Free),
            new WorkspaceMessageAccount(2, PlanCode.Free, null, Free - 1),
            new WorkspaceMessageAccount(3, PlanCode.Maker, null, Maker + 1),
            new WorkspaceMessageAccount(4, PlanCode.Maker, null, Maker - 1));

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.Equal([1], _snapshot!.WorkspaceIds);
        Assert.Equal(Now.UtcDateTime, _snapshot.TakenAtUtc);
        Assert.Equal(
            [new WorkspaceMessageLimitReachedEvent(1, Free, Free, true) { CreatedAtUtc = default }, new WorkspaceMessageLimitReachedEvent(3, Maker, Maker + 1, false) { CreatedAtUtc = default }],
            _events.Cast<WorkspaceMessageLimitReachedEvent>().Select(e => e with { CreatedAtUtc = default }));
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
        _allowance.Setup(x => x.RecordLimitReachedAsync(It.IsAny<IReadOnlyCollection<int>>(), November, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Monitor().CheckAsync(CancellationToken.None);

        Assert.Equal([1], _snapshot!.WorkspaceIds);
        Assert.Empty(_events);
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

    private MessageAllowanceMonitor Monitor() =>
        new(_allowance.Object, new PlanCatalog(Options.Create(new PlanCatalogOptions())), _publisher.Object, _bus.Object, new FixedClock(), NullLogger<MessageAllowanceMonitor>.Instance);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
