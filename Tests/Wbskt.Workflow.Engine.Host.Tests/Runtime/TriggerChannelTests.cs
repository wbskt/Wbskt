using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

/// <summary>
/// One test per channel: what a published trigger is registered under is what that channel's events
/// are matched by, and what a parked branch waits on is the correlation key of the event that wakes it.
/// </summary>
public sealed class TriggerChannelTests
{
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ClientRef = Guid.Parse("7b4d1c1e-0000-4000-8000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonElement Body = JsonSerializer.SerializeToElement(new { value = 1 });

    private static readonly WorkflowDefinitionRow Definition = new()
    {
        Id = 42,
        RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Version = 3,
        WorkspaceId = 7,
        Name = "wf",
        Description = null,
        IsEnabled = true,
        DefinitionJson = "{}",
        PublishedBy = 1,
        CreatedAt = Now
    };

    [Fact]
    public void Every_channel_is_found_by_its_own_kind()
    {
        foreach (ITriggerChannel channel in TriggerChannelRegistry.Default.All)
        {
            Assert.Same(channel, TriggerChannelRegistry.Default.Find(channel.Kind));
        }
    }

    [Fact]
    public async Task Client_message_matches_its_registration_and_the_any_type_one()
    {
        var channel = new ClientChannel();
        InboundEvent evt = ClientChannel.Event(new ClientMessage(ClientRef, 5, 7, "temp", Body, Now, Now, false), "id-1");

        TriggerRegistrationSpec typed = await RegisterAsync(channel, ClientTrigger("temp"));
        TriggerRegistrationSpec any = await RegisterAsync(channel, ClientTrigger(ClientChannel.AnyType));

        Assert.Contains(typed.TriggerKey, evt.MatchKeys);
        Assert.Contains(any.TriggerKey, evt.MatchKeys);
        Assert.Equal($"client:{ClientRef}:temp", channel.CorrelationKey(evt));
    }

    [Fact]
    public async Task A_held_client_trigger_belongs_to_the_hold_channel_alone()
    {
        ClientTriggerNode held = ClientTrigger("temp", holdSeconds: 600);

        Assert.Null(await new ClientChannel().RegisterAsync(held, Publication(), CancellationToken.None));
        TriggerRegistrationSpec spec = await RegisterAsync(new ClientHoldChannel(), held);

        // The recorder finds holds by this prefix, and the ticker reads the hold time back from the key.
        Assert.StartsWith(ClientHoldTriggerKey.Prefix(ClientRef, "temp"), spec.TriggerKey);
        Assert.True(ClientHoldTriggerKey.TryGetHoldSeconds(spec.TriggerKey, out int holdSeconds));
        Assert.Equal(600, holdSeconds);

        // A held message correlates as the message itself would have.
        InboundEvent message = ClientChannel.Event(new ClientMessage(ClientRef, 5, 7, "temp", Body, Now, Now, false), "id-1");
        Assert.Equal(new ClientChannel().CorrelationKey(message), new ClientHoldChannel().CorrelationKey(message with { ChannelKind = ClientHoldChannel.Name }));
    }

    [Fact]
    public async Task Presence_registration_is_found_by_the_parkers_prefix_and_carries_its_grace_period()
    {
        var trigger = new ClientPresenceTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "offline",
            Ports = [],
            Config = new ClientPresenceTriggerConfig { ClientRef = ClientRef.ToString(), State = ClientPresenceState.Offline, ForSeconds = 300 }
        };

        TriggerRegistrationSpec spec = await RegisterAsync(new PresenceChannel(), trigger);

        Assert.StartsWith(ClientPresenceTriggerKey.Prefix(ClientRef, ClientPresenceState.Offline), spec.TriggerKey);
        Assert.True(ClientPresenceTriggerKey.TryGetForSeconds(spec.TriggerKey, out int forSeconds));
        Assert.Equal(300, forSeconds);
    }

    [Fact]
    public async Task Schedule_occurrence_matches_the_registration_for_its_fire()
    {
        var fires = new Mock<IScheduledFireProvider>();
        fires
            .Setup(f => f.InsertAsync(It.IsAny<Guid>(), 42, Definition.RefId, "0 */5 * * * *", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid node, int definitionId, Guid workflowRef, string cron, DateTime next, CancellationToken _) => new ScheduledFireRow
            {
                Id = 17,
                TriggerNodeId = node,
                WorkflowDefinitionId = definitionId,
                WorkflowRefId = workflowRef,
                CronOrInterval = cron,
                NextFireAt = next,
                LeasedUntil = null,
                CreatedAt = Now
            });
        var trigger = new ScheduleTriggerNode { NodeId = Guid.NewGuid(), Name = "every 5", Ports = [], Config = new ScheduleTriggerConfig { Cron = "0 */5 * * * *" } };

        TriggerRegistrationSpec spec = await RegisterAsync(new ScheduleChannel(), trigger, fires.Object);
        InboundEvent evt = ScheduleChannel.Event(new ScheduledFireRow
        {
            Id = 17,
            TriggerNodeId = trigger.NodeId,
            WorkflowDefinitionId = 42,
            WorkflowRefId = Definition.RefId,
            CronOrInterval = "0 */5 * * * *",
            NextFireAt = Now,
            LeasedUntil = null,
            CreatedAt = Now
        }, Now);

        Assert.Contains(spec.TriggerKey, evt.MatchKeys);
        Assert.Equal(spec.TriggerKey, new ScheduleChannel().CorrelationKey(evt));
    }

    [Fact]
    public async Task Webhook_call_matches_the_registration_in_its_own_workspace_only()
    {
        var trigger = new WebhookTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "hook",
            Ports = [],
            Config = new WebhookTriggerConfig { Path = "orders", Method = "POST", Secret = "s3cret" }
        };

        TriggerRegistrationSpec spec = await RegisterAsync(new WebhookChannel(), trigger);
        InboundEvent evt = WebhookChannel.Event(WorkspaceRef, "orders", Body, "id-1", "s3cret");
        InboundEvent otherWorkspace = WebhookChannel.Event(Guid.NewGuid(), "orders", Body, "id-2", "s3cret");

        Assert.Contains(spec.TriggerKey, evt.MatchKeys);
        Assert.DoesNotContain(spec.TriggerKey, otherWorkspace.MatchKeys);
        Assert.Equal(spec.TriggerKey, new WebhookChannel().CorrelationKey(evt));
        Assert.Equal("s3cret", spec.WebhookSecret);
        Assert.False(evt.Payload.ContainsKey("secret"));
    }

    [Fact]
    public async Task Manual_request_matches_the_registration_of_its_workflow_and_a_request_key_is_stable()
    {
        var trigger = new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "run", Ports = [], Config = new ManualTriggerConfig { Description = "run" } };

        TriggerRegistrationSpec spec = await RegisterAsync(new ManualChannel(), trigger);
        InboundEvent evt = ManualChannel.Event(Definition.RefId, Body, "retry-1", Now);

        Assert.Contains(spec.TriggerKey, evt.MatchKeys);
        Assert.Equal(spec.TriggerKey, new ManualChannel().CorrelationKey(evt));
        Assert.Equal(evt.InboundEventId, ManualChannel.Event(Definition.RefId, Body, "retry-1", Now).InboundEventId);
        Assert.NotEqual(ManualChannel.Event(Definition.RefId, Body, null, Now).InboundEventId, ManualChannel.Event(Definition.RefId, Body, null, Now).InboundEventId);
    }

    [Fact]
    public void Signal_wakes_the_branch_waiting_on_its_name_in_its_run()
    {
        Guid run = Guid.NewGuid();
        InboundEvent evt = SignalChannel.Event(run, "approved", Body);

        Assert.Equal(SignalChannel.Key("approved", run.ToString()), new SignalChannel().CorrelationKey(evt));
        Assert.Contains(SignalChannel.Key("approved", run.ToString()), evt.MatchKeys);
    }

    [Fact]
    public void Http_wake_wakes_the_branch_waiting_on_its_token()
    {
        InboundEvent evt = HttpWakeChannel.Event("tok-1", Body);

        Assert.Equal(HttpWakeChannel.Key("tok-1"), new HttpWakeChannel().CorrelationKey(evt));
    }

    [Fact]
    public void Child_completion_wakes_the_parent_waiting_on_that_child()
    {
        Guid child = Guid.NewGuid();
        InboundEvent evt = ChildCompletedChannel.Event(child, RunStatus.Succeeded, Guid.NewGuid());

        Assert.Equal(ChildCompletedChannel.Key(child.ToString()), new ChildCompletedChannel().CorrelationKey(evt));
    }

    [Fact]
    public void A_command_outcome_wakes_the_step_waiting_on_that_command()
    {
        Guid command = Guid.NewGuid();
        InboundEvent acked = CommandChannel.Event(command, CommandChannel.Acked, null);
        InboundEvent failed = CommandChannel.Event(command, CommandChannel.Failed, "The device is not connected.");

        Assert.Equal(CommandChannel.Key(command), new CommandChannel().CorrelationKey(acked));
        Assert.Contains(CommandChannel.Key(command), failed.MatchKeys);
        // An ack and a failure for one command are different events, so neither is dropped as the other's duplicate.
        Assert.NotEqual(acked.InboundEventId, failed.InboundEventId);
    }

    [Fact]
    public async Task Wake_only_channels_register_nothing_and_no_two_channels_claim_one_node()
    {
        BaseNode[] triggers =
        [
            ClientTrigger("temp"),
            ClientTrigger("temp", holdSeconds: 60),
            new WebhookTriggerNode { NodeId = Guid.NewGuid(), Name = "hook", Ports = [], Config = new WebhookTriggerConfig { Path = "p", Method = "POST" } },
            new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "run", Ports = [], Config = new ManualTriggerConfig { Description = "run" } }
        ];

        foreach (BaseNode node in triggers)
        {
            int claims = 0;
            foreach (ITriggerChannel channel in TriggerChannelRegistry.Default.All)
            {
                claims += await channel.RegisterAsync(node, Publication(), CancellationToken.None) is null ? 0 : 1;
            }

            Assert.Equal(1, claims);
        }
    }

    private static ClientTriggerNode ClientTrigger(string type, int holdSeconds = 0) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "client",
        Ports = [],
        Config = new ClientTriggerConfig { ClientRef = ClientRef.ToString(), Type = type, HoldSeconds = holdSeconds }
    };

    private static TriggerPublication Publication(IScheduledFireProvider? fires = null) =>
        new(Definition, WorkspaceRef, fires ?? Mock.Of<IScheduledFireProvider>(), Mock.Of<IClock>(c => c.UtcNow == Now));

    private static async Task<TriggerRegistrationSpec> RegisterAsync(ITriggerChannel channel, BaseNode node, IScheduledFireProvider? fires = null)
    {
        TriggerRegistrationSpec? spec = await channel.RegisterAsync(node, Publication(fires), CancellationToken.None);
        Assert.NotNull(spec);
        return spec;
    }
}
