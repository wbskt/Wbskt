using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class ClientPresenceTickerTests
{
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime ChangedAt = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = ChangedAt.AddMinutes(1);

    // ------------------------------------------------------------------ offline

    [Fact]
    public void Offline_fires_when_the_client_is_still_disconnected_from_that_disconnect()
    {
        var check = Offline(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt);

        Assert.Equal(PresenceCheckVerdict.Fire, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Offline_is_stale_when_the_client_came_back_within_the_grace_period()
    {
        var check = Offline(isConnected: true, connectedAt: ChangedAt.AddSeconds(20), lastActivityAt: ChangedAt.AddSeconds(20));

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Offline_is_stale_when_a_later_disconnect_took_over()
    {
        // Reconnected and dropped again: the later disconnect parked its own check.
        var check = Offline(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt.AddSeconds(40));

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Offline_waits_while_the_client_row_still_shows_the_old_connection()
    {
        // The management host has not recorded the disconnect yet.
        var check = Offline(isConnected: true, connectedAt: ChangedAt.AddHours(-1), lastActivityAt: ChangedAt.AddSeconds(-5));

        Assert.Equal(PresenceCheckVerdict.Pending, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void A_check_that_never_catches_up_is_given_up_on()
    {
        var check = Offline(isConnected: true, connectedAt: ChangedAt.AddHours(-1), lastActivityAt: ChangedAt.AddSeconds(-5));

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, check.DueAt + ClientPresenceTicker.GiveUpAfter + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_deleted_client_makes_the_check_stale()
    {
        var check = Offline(isConnected: null, connectedAt: null, lastActivityAt: null) with { ClientId = null, ClientWorkspaceId = null };

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Datetime_rounding_does_not_count_as_a_later_change()
    {
        // Both sides go through SQL datetime types, which can shift a value by a few milliseconds.
        var check = Offline(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt.AddMilliseconds(3));

        Assert.Equal(PresenceCheckVerdict.Fire, ClientPresenceTicker.Evaluate(check, Now));
    }

    // ------------------------------------------------------------------ online

    [Fact]
    public void Online_fires_when_the_same_connection_is_still_up()
    {
        var check = Online(isConnected: true, connectedAt: ChangedAt, lastActivityAt: ChangedAt.AddSeconds(30));

        Assert.Equal(PresenceCheckVerdict.Fire, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Online_is_stale_when_the_client_dropped_again()
    {
        var check = Online(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt.AddSeconds(10));

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Online_is_stale_when_a_newer_connection_replaced_it()
    {
        var check = Online(isConnected: true, connectedAt: ChangedAt.AddSeconds(15), lastActivityAt: ChangedAt.AddSeconds(15));

        Assert.Equal(PresenceCheckVerdict.Stale, ClientPresenceTicker.Evaluate(check, Now));
    }

    [Fact]
    public void Online_waits_while_the_client_row_has_not_recorded_the_connect()
    {
        var check = Online(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt.AddMinutes(-10));

        Assert.Equal(PresenceCheckVerdict.Pending, ClientPresenceTicker.Evaluate(check, Now));
    }

    // ------------------------------------------------------------------ ticking

    [Fact]
    public async Task Tick_without_the_lease_does_nothing()
    {
        var provider = new RecordingPresenceCheckProvider { Due = [Offline(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt)] };
        var hub = new RecordingInboundHub();

        await CreateTicker(provider, hub, isLeader: false).ProcessDueChecksAsync(CancellationToken.None);

        Assert.Empty(hub.Events);
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task Tick_dispatches_a_firing_check_to_its_trigger_and_deletes_it()
    {
        ClientPresenceCheckRow check = Offline(isConnected: false, connectedAt: null, lastActivityAt: ChangedAt);
        var provider = new RecordingPresenceCheckProvider { Due = [check] };
        var hub = new RecordingInboundHub();

        await CreateTicker(provider, hub).ProcessDueChecksAsync(CancellationToken.None);

        InboundEvent evt = Assert.Single(hub.Events);
        Assert.Equal("presence", evt.ChannelKind);
        Assert.Equal([check.TriggerKey], evt.MatchKeys);
        Assert.Equal($"presence:{check.Id}", evt.InboundEventId);
        Assert.Equal(ClientRef, evt.Payload["clientRefId"].GetGuid());
        Assert.Equal("offline", evt.Payload["state"].GetString());
        Assert.Equal(60, evt.Payload["forSeconds"].GetInt32());
        Assert.Equal(34, evt.Payload["workspaceId"].GetInt32());
        Assert.Equal([check.Id], provider.Deleted);
    }

    [Fact]
    public async Task Tick_deletes_a_stale_check_without_dispatching_and_keeps_a_pending_one()
    {
        ClientPresenceCheckRow stale = Offline(isConnected: true, connectedAt: ChangedAt.AddSeconds(20), lastActivityAt: ChangedAt.AddSeconds(20)) with { Id = 1 };
        ClientPresenceCheckRow pending = Offline(isConnected: true, connectedAt: ChangedAt.AddHours(-1), lastActivityAt: ChangedAt.AddSeconds(-5)) with { Id = 2 };
        var provider = new RecordingPresenceCheckProvider { Due = [stale, pending] };
        var hub = new RecordingInboundHub();

        await CreateTicker(provider, hub).ProcessDueChecksAsync(CancellationToken.None);

        Assert.Empty(hub.Events);
        Assert.Equal([1], provider.Deleted);
    }

    private static ClientPresenceTicker CreateTicker(RecordingPresenceCheckProvider provider, RecordingInboundHub hub, bool isLeader = true)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IClientPresenceCheckProvider>(provider)
            .AddSingleton<IInboundHub>(hub)
            .BuildServiceProvider();

        return new ClientPresenceTicker(
            new FixedClock(Now),
            new FixedLeaseHolder(isLeader),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ClientPresenceTicker>.Instance,
            Options.Create(new WorkflowEngineOptions()));
    }

    private static ClientPresenceCheckRow Offline(bool? isConnected, DateTime? connectedAt, DateTime? lastActivityAt) =>
        Check(ClientPresenceState.Offline, 60, isConnected, connectedAt, lastActivityAt);

    private static ClientPresenceCheckRow Online(bool? isConnected, DateTime? connectedAt, DateTime? lastActivityAt) =>
        Check(ClientPresenceState.Online, 0, isConnected, connectedAt, lastActivityAt);

    private static ClientPresenceCheckRow Check(ClientPresenceState state, int forSeconds, bool? isConnected, DateTime? connectedAt, DateTime? lastActivityAt) => new()
    {
        Id = 7,
        TriggerKey = ClientPresenceTriggerKey.Build(ClientRef, state, forSeconds, Guid.NewGuid(), Guid.NewGuid()),
        ClientRefId = ClientRef,
        State = ClientPresenceTriggerKey.StateName(state),
        ChangedAt = ChangedAt,
        DueAt = ChangedAt.AddSeconds(forSeconds),
        ClientId = 12,
        ClientWorkspaceId = 34,
        ClientIsConnected = isConnected,
        ClientConnectedAt = connectedAt,
        ClientLastActivityAt = lastActivityAt
    };

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FixedLeaseHolder(bool isHeld) : ILeaseHolder
    {
        public Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct) => Task.FromResult(isHeld);
        public Task ReleaseAsync(string leaseName, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct) => Task.FromResult(isHeld);
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
