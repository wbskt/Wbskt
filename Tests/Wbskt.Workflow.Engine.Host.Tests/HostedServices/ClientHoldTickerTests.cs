using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class ClientHoldTickerTests
{
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime SinceAt = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = SinceAt.AddMinutes(11);

    [Fact]
    public async Task A_due_hold_dispatches_the_held_message_and_is_marked_fired()
    {
        ClientHoldStateRow hold = Hold(id: 7, holdSeconds: 600);
        var provider = new RecordingHoldStateProvider { Due = [hold] };
        var hub = new RecordingInboundHub(TriggerDispatchOutcome.StartedRun);

        await CreateTicker(provider, hub).ProcessDueHoldsAsync(CancellationToken.None);

        InboundEvent evt = Assert.Single(hub.Events);
        Assert.Equal(ClientHoldTriggerKey.TriggerKind, evt.ChannelKind);
        Assert.Equal([hold.TriggerKey], evt.MatchKeys);
        Assert.Equal($"client-hold:7:{SinceAt.Ticks}", evt.InboundEventId);
        Assert.Equal("temperature", evt.Payload["messageType"].GetString());
        Assert.Equal(9.5, evt.Payload["payload"].GetProperty("celsius").GetDouble());
        Assert.Equal(SinceAt, evt.Payload["heldSince"].GetDateTime());
        Assert.Equal(600, evt.Payload["holdSeconds"].GetInt32());
        Assert.Equal([(7, SinceAt)], provider.Fired);
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task A_hold_whose_trigger_is_gone_is_deleted_instead_of_fired()
    {
        var provider = new RecordingHoldStateProvider { Due = [Hold(id: 3, holdSeconds: 60)] };
        var hub = new RecordingInboundHub(TriggerDispatchOutcome.NoRegistration);

        await CreateTicker(provider, hub).ProcessDueHoldsAsync(CancellationToken.None);

        Assert.Equal([3], provider.Deleted);
        Assert.Empty(provider.Fired);
    }

    [Fact]
    public async Task A_redelivered_hold_dropped_as_a_duplicate_is_still_marked_fired()
    {
        // The process died after dispatching but before marking the hold fired; the re-leased hold
        // reuses its id, so the dispatcher drops it and the hold must still end up fired.
        var provider = new RecordingHoldStateProvider { Due = [Hold(id: 4, holdSeconds: 60)] };
        var hub = new RecordingInboundHub(TriggerDispatchOutcome.Idempotent);

        await CreateTicker(provider, hub).ProcessDueHoldsAsync(CancellationToken.None);

        Assert.Equal([(4, SinceAt)], provider.Fired);
    }

    [Fact]
    public async Task Nothing_happens_on_a_follower()
    {
        var provider = new RecordingHoldStateProvider { Due = [Hold(id: 1, holdSeconds: 60)] };
        var hub = new RecordingInboundHub(TriggerDispatchOutcome.StartedRun);

        await CreateTicker(provider, hub, isLeader: false).ProcessDueHoldsAsync(CancellationToken.None);

        Assert.Empty(hub.Events);
        Assert.Empty(provider.Fired);
    }

    [Fact]
    public void A_later_hold_of_the_same_trigger_gets_a_different_event_id()
    {
        string first = ClientHoldTicker.ToInboundEvent(Hold(id: 1, holdSeconds: 60), Now).InboundEventId;
        string second = ClientHoldTicker.ToInboundEvent(Hold(id: 1, holdSeconds: 60) with { SinceAt = SinceAt.AddHours(1) }, Now).InboundEventId;

        Assert.NotEqual(first, second);
    }

    private static ClientHoldTicker CreateTicker(RecordingHoldStateProvider provider, RecordingInboundHub hub, bool isLeader = true)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IClientHoldStateProvider>(provider)
            .AddSingleton<IInboundHub>(hub)
            .BuildServiceProvider();

        return new ClientHoldTicker(
            new FixedClock(Now),
            new FixedLeaseHolder(isLeader),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ClientHoldTicker>.Instance,
            Options.Create(new WorkflowEngineOptions()));
    }

    private static ClientHoldStateRow Hold(int id, int holdSeconds) => new()
    {
        Id = id,
        TriggerKey = ClientHoldTriggerKey.Build(ClientRef, "temperature", holdSeconds, Guid.NewGuid(), Guid.NewGuid()),
        SinceAt = SinceAt,
        DueAt = SinceAt.AddSeconds(holdSeconds),
        Payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["messageType"] = "temperature",
            ["clientRefId"] = ClientRef,
            ["clientId"] = 12,
            ["workspaceId"] = 34,
            ["payload"] = new { celsius = 9.5 }
        })
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

    private sealed class RecordingInboundHub(TriggerDispatchOutcome outcome) : IInboundHub
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(new TriggerDispatchResult(outcome, null, null, "ok"));
        }
    }
}
