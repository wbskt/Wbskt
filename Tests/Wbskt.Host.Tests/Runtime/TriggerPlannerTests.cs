using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Host.Tests.Runtime;

/// <summary>
/// What making a version live registers. The planner is pure: it reads the definition and the clock and
/// returns the plan <c>dbo.Workflow_Enable</c> writes, so every trigger kind is checked here without SQL.
/// </summary>
public sealed class TriggerPlannerTests
{
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid WorkflowRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Plans_one_registration_per_trigger_node()
    {
        TriggerPlan plan = Plan(CreateDefinition());

        Assert.Equal(4, plan.Registrations.Count);
        Assert.Contains(plan.Registrations, row => row.TriggerKind == "client" && row.TriggerKey == "client:client-serial-1:telemetry");
        Assert.Contains(plan.Registrations, row => row.TriggerKind == "webhook" && row.TriggerKey == $"webhook:{WorkspaceRef}:/hooks/intake");
        Assert.Contains(plan.Registrations, row => row.TriggerKind == "manual" && row.TriggerKey == $"manual:{WorkflowRef}");
        Assert.Contains(plan.Registrations, row => row.TriggerKind == "schedule" && row.TriggerKey == $"schedule:{WorkflowRef}:22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void A_schedule_key_names_the_workflow_not_a_version_so_enabling_another_version_replaces_it()
    {
        var definition = CreateDefinition();

        TriggerPlan v3 = Plan(definition);
        TriggerPlan v4 = Plan(definition with { Version = 4 });

        Assert.Equal(
            v3.Registrations.Single(r => r.TriggerKind == "schedule").TriggerKey,
            v4.Registrations.Single(r => r.TriggerKind == "schedule").TriggerKey);
    }

    [Fact]
    public void A_definition_without_triggers_plans_nothing()
    {
        TriggerPlan plan = Plan(CreateDefinition() with { Nodes = [] });

        Assert.Empty(plan.Registrations);
        Assert.Empty(plan.Schedules);
    }

    [Fact]
    public void A_schedule_trigger_plans_its_first_fire_from_the_clock()
    {
        PlannedSchedule schedule = Assert.Single(Plan(CreateDefinition()).Schedules);

        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), schedule.TriggerNodeId);
        Assert.Equal("*/5 * * * *", schedule.CronOrInterval);
        Assert.Equal(new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), schedule.NextFireAt);
    }

    [Fact]
    public void A_six_field_cron_plans_its_first_fire_too()
    {
        // A 6-field cron (leading seconds) must parse when it is made live too, not just at tick time.
        var definition = CreateDefinition() with
        {
            Nodes =
            [
                new ScheduleTriggerNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "schedule", Ports = [], Config = new ScheduleTriggerConfig { Cron = "0 */5 * * * *" } }
            ]
        };

        PlannedSchedule schedule = Assert.Single(Plan(definition).Schedules);

        Assert.Equal(new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), schedule.NextFireAt);
    }

    [Fact]
    public void The_filter_and_secret_are_copied_onto_the_registration()
    {
        // The dispatcher matches on the row, so anything it needs at dispatch time has to be copied
        // here - otherwise it would load and deserialize a whole definition per candidate registration.
        WorkflowExpression filter = new BinaryExpression(
            new BranchStateRefExpression("status"),
            BinaryOperator.Equal,
            new LiteralExpression("active"));
        var definition = CreateDefinition() with
        {
            Nodes =
            [
                new WebhookTriggerNode { NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "webhook", Ports = [], Config = new WebhookTriggerConfig { Path = "/hooks/intake", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.DropIfRunning, Filter = filter, Secret = "s3cret" } },
                new ClientTriggerNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "client", Ports = [], Config = new ClientTriggerConfig { ClientRef = "client-serial-1", Type = "telemetry", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue, Filter = filter } }
            ]
        };
        TriggerPlan plan = Plan(definition);
        PlannedTrigger webhook = plan.Registrations.Single(r => r.TriggerKind == "webhook");
        PlannedTrigger client = plan.Registrations.Single(r => r.TriggerKind == "client");

        // Round-trips as a structured expression, not as some stringified shorthand.
        Assert.NotNull(webhook.FilterExpression);
        Assert.IsType<BinaryExpression>(JsonSerializer.Deserialize<WorkflowExpression>(webhook.FilterExpression!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("s3cret", webhook.WebhookSecret);

        Assert.NotNull(client.FilterExpression);
        // A secret is a webhook-only notion; a client trigger must not acquire one.
        Assert.Null(client.WebhookSecret);
    }

    [Fact]
    public void Filter_and_secret_stay_null_when_the_author_configured_neither()
    {
        Assert.All(Plan(CreateDefinition()).Registrations, row =>
        {
            Assert.Null(row.FilterExpression);
            Assert.Null(row.WebhookSecret);
        });
    }

    private static WorkflowDefinition CreateDefinition()
    {
        return new WorkflowDefinition(
            WorkflowRef,
            3,
            9,
            "definition",
            null,
            [
                new ClientTriggerNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "client", Ports = [], Config = new ClientTriggerConfig { ClientRef = "client-serial-1", Type = "telemetry", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue } },
                new ScheduleTriggerNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "schedule", Ports = [], Config = new ScheduleTriggerConfig { Cron = "*/5 * * * *" } },
                new WebhookTriggerNode { NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "webhook", Ports = [], Config = new WebhookTriggerConfig { Path = "/hooks/intake", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.DropIfRunning } },
                new ManualTriggerNode { NodeId = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "manual", Ports = [], Config = new ManualTriggerConfig { Description = "manual" } }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            7);
    }

    [Fact]
    public void A_presence_trigger_plans_a_key_naming_the_trigger_and_its_grace_period()
    {
        var clientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var nodeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var definition = CreateDefinition() with
        {
            Nodes =
            [
                // Upper case on purpose: the key must use the canonical form the socket host reports.
                new ClientPresenceTriggerNode { NodeId = nodeId, Name = "offline", Ports = [], Config = new ClientPresenceTriggerConfig { ClientRef = clientRef.ToString().ToUpperInvariant(), State = ClientPresenceState.Offline, ForSeconds = 300 } }
            ]
        };
        PlannedTrigger row = Assert.Single(Plan(definition).Registrations);
        Assert.Equal("presence", row.TriggerKind);
        Assert.StartsWith(ClientPresenceTriggerKey.Prefix(clientRef, ClientPresenceState.Offline), row.TriggerKey);
        Assert.EndsWith($":300:{WorkflowRef}:{nodeId}", row.TriggerKey);
        Assert.True(ClientPresenceTriggerKey.TryGetForSeconds(row.TriggerKey, out int forSeconds));
        Assert.Equal(300, forSeconds);
        Assert.Equal(nameof(WorkflowConcurrencyPolicy.Queue), row.ConcurrencyPolicy);
    }

    [Fact]
    public void A_held_client_trigger_plans_a_hold_key_instead_of_a_client_key()
    {
        var clientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var nodeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var definition = CreateDefinition() with
        {
            Nodes =
            [
                new ClientTriggerNode { NodeId = nodeId, Name = "too warm", Ports = [], Config = new ClientTriggerConfig { ClientRef = clientRef.ToString().ToUpperInvariant(), Type = "temp_c", HoldSeconds = 600, Filter = new LiteralExpression(true) } }
            ]
        };
        PlannedTrigger row = Assert.Single(Plan(definition).Registrations);
        Assert.Equal(ClientHoldTriggerKey.TriggerKind, row.TriggerKind);
        Assert.Equal(ClientHoldTriggerKey.Build(clientRef, "temp_c", 600, WorkflowRef, nodeId), row.TriggerKey);
        Assert.True(ClientHoldTriggerKey.TryGetHoldSeconds(row.TriggerKey, out int holdSeconds));
        Assert.Equal(600, holdSeconds);
        Assert.NotNull(row.FilterExpression);
    }

    private static TriggerPlan Plan(WorkflowDefinition definition) =>
        new TriggerPlanner(TriggerChannelRegistry.Default, new FixedClock()).Plan(definition, WorkflowRef, WorkspaceRef);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 5, 26, 12, 30, 0, TimeSpan.Zero);
    }
}
