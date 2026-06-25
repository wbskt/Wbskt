using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class ScheduledFireTickerTests
{
    [Fact]
    public async Task Tick_no_lease_skips()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: false);
        var provider = new RecordingScheduledFireProvider([]);
        var inboundHub = new RecordingInboundHub();
        var ticker = new ScheduledFireTicker(new FixedClock(), leaseHolder, provider, inboundHub, NullLogger<ScheduledFireTicker>.Instance);

        // Act
        await ticker.ProcessScheduledFiresAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["schedule-tick"], leaseHolder.IsHeldCalls);
        Assert.Equal(0, provider.LeaseDueCalls);
        Assert.Empty(inboundHub.Events);
    }

    [Fact]
    public async Task Tick_leases_due_and_dispatches_inbound()
    {
        // Arrange
        var fire = CreateFire(41, "0 */5 * * * *", new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var provider = new RecordingScheduledFireProvider([fire]);
        var inboundHub = new RecordingInboundHub();
        var ticker = new ScheduledFireTicker(new FixedClock(), leaseHolder, provider, inboundHub, NullLogger<ScheduledFireTicker>.Instance);

        // Act
        await ticker.ProcessScheduledFiresAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, provider.LeaseDueCalls);
        Assert.Equal((120, 64), provider.LastLeaseRequest);
        InboundEvent evt = Assert.Single(inboundHub.Events);
        Assert.Equal("schedule", evt.ChannelKind);
        Assert.Contains("schedule:41", evt.MatchKeys);
        Assert.Equal(new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc), evt.ReceivedAt);
        Assert.Equal(41, evt.Payload["scheduledFireId"].GetInt32());
        Assert.Equal(fire.WorkflowRefId, evt.Payload["definitionRefId"].GetGuid());
        Assert.Equal(fire.NextFireAt, evt.Payload["fireAt"].GetDateTime());
    }

    [Fact]
    public async Task Tick_advances_recurring_fire()
    {
        // Arrange
        var fire = CreateFire(42, "0 */5 * * * *", new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var provider = new RecordingScheduledFireProvider([fire]);
        var inboundHub = new RecordingInboundHub();
        var ticker = new ScheduledFireTicker(new FixedClock(), leaseHolder, provider, inboundHub, NullLogger<ScheduledFireTicker>.Instance);

        // Act
        await ticker.ProcessScheduledFiresAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(42, new DateTime(2026, 5, 26, 12, 5, 0, DateTimeKind.Utc))], provider.AdvancedFires);
        Assert.Empty(provider.DeletedFireIds);
    }

    [Fact]
    public async Task Tick_deletes_one_shot_fire()
    {
        // Arrange
        var fire = CreateFire(43, string.Empty, new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var provider = new RecordingScheduledFireProvider([fire]);
        var inboundHub = new RecordingInboundHub();
        var ticker = new ScheduledFireTicker(new FixedClock(), leaseHolder, provider, inboundHub, NullLogger<ScheduledFireTicker>.Instance);

        // Act
        await ticker.ProcessScheduledFiresAsync(CancellationToken.None);

        // Assert
        Assert.Equal([43L], provider.DeletedFireIds);
        Assert.Empty(provider.AdvancedFires);
    }

    private static ScheduledFireRow CreateFire(int id, string cronOrInterval, DateTime nextFireAt)
    {
        return new ScheduledFireRow
        {
            Id = id,
            TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            WorkflowDefinitionId = 7,
            WorkflowRefId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CronOrInterval = cronOrInterval,
            NextFireAt = nextFireAt,
            LeasedUntil = null,
            CreatedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingLeaseHolder(bool isHeld) : ILeaseHolder
    {
        public List<string> IsHeldCalls { get; } = [];

        public Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct) => Task.FromResult(isHeld);

        public Task ReleaseAsync(string leaseName, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct)
        {
            IsHeldCalls.Add(leaseName);
            return Task.FromResult(isHeld);
        }
    }

    private sealed class RecordingScheduledFireProvider(IReadOnlyCollection<ScheduledFireRow> fires) : IScheduledFireProvider
    {
        public int LeaseDueCalls { get; private set; }
        public (int LeaseSec, int Batch)? LastLeaseRequest { get; private set; }
        public List<(int Id, DateTime NextFireAt)> AdvancedFires { get; } = [];
        public List<long> DeletedFireIds { get; } = [];

        public Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct)
        {
            LeaseDueCalls++;
            LastLeaseRequest = (leaseSec, batch);
            return Task.FromResult(fires);
        }

        public Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct)
        {
            AdvancedFires.Add((id, nextFireAt));
            return Task.FromResult(fires.Single(fire => fire.Id == id));
        }

        public Task DeleteByIdAsync(long id, CancellationToken ct)
        {
            DeletedFireIds.Add(id);
            return Task.CompletedTask;
        }

        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingInboundHub : IInboundHub
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        }
    }
}
