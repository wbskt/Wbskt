using FluentAssertions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkspaceMessageCounterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private const int WorkspaceId = 3;
    private const int OtherWorkspaceId = 4;

    [Fact]
    public void Received_messages_and_delivered_commands_count_per_workspace()
    {
        var time = new FixedTimeProvider(Now);
        var counter = new WorkspaceMessageCounter(time);

        for (var i = 0; i < 10; i++)
        {
            counter.Count(Received(WorkspaceId));
        }
        counter.Count(Delivered(WorkspaceId));
        counter.Count(Received(OtherWorkspaceId));
        counter.Count(new ClientRenamedEvent(Guid.NewGuid(), 1, WorkspaceId, "a", "b"));

        time.Now = Now.AddSeconds(5);
        var rates = counter.Flush().ToDictionary(r => r.WorkspaceId, r => r.Rate);

        rates[WorkspaceId].InboundPerSecond.Should().Be(2);
        rates[WorkspaceId].OutboundPerSecond.Should().Be(0.2);
        rates[WorkspaceId].MessagesPerSecond.Should().Be(2.2);
        rates[WorkspaceId].WindowSeconds.Should().Be(5);
        rates[OtherWorkspaceId].InboundPerSecond.Should().Be(0.2);
        counter.Current(WorkspaceId).Should().Be(rates[WorkspaceId]);
    }

    [Fact]
    public void A_workspace_that_goes_quiet_gets_one_zero_then_nothing()
    {
        var time = new FixedTimeProvider(Now);
        var counter = new WorkspaceMessageCounter(time);
        counter.Count(Received(WorkspaceId));

        time.Now = Now.AddSeconds(5);
        counter.Flush().Should().ContainSingle();

        time.Now = Now.AddSeconds(10);
        counter.Flush().Should().ContainSingle().Which.Rate.MessagesPerSecond.Should().Be(0);

        time.Now = Now.AddSeconds(15);
        counter.Flush().Should().BeEmpty();
        counter.Current(WorkspaceId).MessagesPerSecond.Should().Be(0);
    }

    [Fact]
    public void A_late_tick_divides_by_the_time_that_really_passed()
    {
        var time = new FixedTimeProvider(Now);
        var counter = new WorkspaceMessageCounter(time);
        for (var i = 0; i < 10; i++)
        {
            counter.Count(Received(WorkspaceId));
        }

        time.Now = Now.AddSeconds(10);

        counter.Flush().Should().ContainSingle().Which.Rate.InboundPerSecond.Should().Be(1);
    }

    [Fact]
    public void A_workspace_with_no_traffic_reads_as_zero()
    {
        new WorkspaceMessageCounter(new FixedTimeProvider(Now)).Current(WorkspaceId).MessagesPerSecond.Should().Be(0);
    }

    private static ClientMessageReceivedEvent Received(int workspaceId)
    {
        return new ClientMessageReceivedEvent(Guid.NewGuid(), 1, workspaceId, "reading", "{}");
    }

    private static ClientCommandDeliveredEvent Delivered(int workspaceId)
    {
        return new ClientCommandDeliveredEvent(Guid.NewGuid(), 1, workspaceId, "relay.set");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
