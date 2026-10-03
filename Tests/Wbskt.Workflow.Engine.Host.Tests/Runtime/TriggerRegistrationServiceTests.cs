using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerRegistrationServiceTests
{
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task OnPublished_inserts_one_registration_per_trigger_node()
    {
        // Arrange
        var workflowDefinitionProvider = new RecordingWorkflowDefinitionProvider(CreateDefinition());
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var service = new TriggerRegistrationService(workflowDefinitionProvider, triggerRegistrationProvider, new RecordingScheduledFireProvider(), new FixedClock());

        // Act
        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        // Assert
        Assert.Equal(4, triggerRegistrationProvider.Rows.Count);
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "client" && row.TriggerKey == "client:client-serial-1:telemetry");
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "webhook" && row.TriggerKey == $"webhook:{WorkspaceRef}:/hooks/intake");
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "manual" && row.TriggerKey == "manual:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "schedule" && row.TriggerKey == "schedule:1");
    }

    [Fact]
    public async Task OnPublished_with_schedule_trigger_inserts_scheduled_fire()
    {
        // Arrange
        var scheduledFireProvider = new RecordingScheduledFireProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(CreateDefinition()), new RecordingTriggerRegistrationProvider(), scheduledFireProvider, new FixedClock());

        // Act
        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        // Assert
        Assert.Single(scheduledFireProvider.InsertCalls);
        Assert.Equal("*/5 * * * *", scheduledFireProvider.InsertCalls.Single().Cron);
        Assert.Equal(new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), scheduledFireProvider.InsertCalls.Single().NextFireAt);
    }

    [Fact]
    public async Task OnPublished_with_6field_cron_computes_next_fire_time()
    {
        // Arrange: a 6-field cron (leading seconds) must parse at publish time too, not just at tick time.
        var definition = CreateDefinition() with
        {
            Nodes =
            [
                new ScheduleTriggerNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "schedule", Ports = [], Config = new ScheduleTriggerConfig { Cron = "0 */5 * * * *" } }
            ]
        };
        var scheduledFireProvider = new RecordingScheduledFireProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(definition), new RecordingTriggerRegistrationProvider(), scheduledFireProvider, new FixedClock());

        // Act
        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        // Assert
        Assert.Single(scheduledFireProvider.InsertCalls);
        Assert.Equal(new DateTime(2026, 5, 26, 12, 35, 0, DateTimeKind.Utc), scheduledFireProvider.InsertCalls.Single().NextFireAt);
    }

    [Fact]
    public async Task OnDeprecated_deactivates_registrations_and_deletes_scheduled_fires()
    {
        // Arrange
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var scheduledFireProvider = new RecordingScheduledFireProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(CreateDefinition()), triggerRegistrationProvider, scheduledFireProvider, new FixedClock());

        // Act
        await service.OnDeprecatedAsync(42, CancellationToken.None);

        // Assert
        Assert.Equal([42], triggerRegistrationProvider.DeletedWorkflowDefinitionIds);
        Assert.Equal([42], scheduledFireProvider.DeletedWorkflowDefinitionIds);
    }

    [Fact]
    public async Task OnPublished_copies_the_filter_and_secret_onto_the_registration()
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
                new WebhookTriggerNode { NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "webhook", Ports = [], Config = new WebhookTriggerConfig { Path = "/hooks/intake", Method = "POST", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.DropIfRunning, Filter = filter, Secret = "s3cret" } },
                new ClientTriggerNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "client", Ports = [], Config = new ClientTriggerConfig { ClientRef = "client-serial-1", Type = "telemetry", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue, Filter = filter } }
            ]
        };
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(definition), triggerRegistrationProvider, new RecordingScheduledFireProvider(), new FixedClock());

        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        TriggerRegistrationRow webhook = triggerRegistrationProvider.Rows.Single(r => r.TriggerKind == "webhook");
        TriggerRegistrationRow client = triggerRegistrationProvider.Rows.Single(r => r.TriggerKind == "client");

        // Round-trips as a structured expression, not as some stringified shorthand.
        Assert.NotNull(webhook.FilterExpression);
        Assert.IsType<BinaryExpression>(JsonSerializer.Deserialize<WorkflowExpression>(webhook.FilterExpression!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("s3cret", webhook.WebhookSecret);

        Assert.NotNull(client.FilterExpression);
        // A secret is a webhook-only notion; a client trigger must not acquire one.
        Assert.Null(client.WebhookSecret);
    }

    [Fact]
    public async Task OnPublished_leaves_filter_and_secret_null_when_the_author_configured_neither()
    {
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(CreateDefinition()), triggerRegistrationProvider, new RecordingScheduledFireProvider(), new FixedClock());

        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        Assert.All(triggerRegistrationProvider.Rows, row =>
        {
            Assert.Null(row.FilterExpression);
            Assert.Null(row.WebhookSecret);
        });
    }

    private static WorkflowDefinition CreateDefinition()
    {
        return new WorkflowDefinition(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            3,
            9,
            "definition",
            null,
            true,
            [
                new ClientTriggerNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "client", Ports = [], Config = new ClientTriggerConfig { ClientRef = "client-serial-1", Type = "telemetry", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue } },
                new ScheduleTriggerNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "schedule", Ports = [], Config = new ScheduleTriggerConfig { Cron = "*/5 * * * *" } },
                new WebhookTriggerNode { NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "webhook", Ports = [], Config = new WebhookTriggerConfig { Path = "/hooks/intake", Method = "POST", CorrelationKey = null, ConcurrencyPolicy = WorkflowConcurrencyPolicy.DropIfRunning } },
                new ManualTriggerNode { NodeId = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "manual", Ports = [], Config = new ManualTriggerConfig { Description = "manual" } }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            7);
    }

    [Fact]
    public async Task OnPublished_with_presence_trigger_registers_a_key_naming_the_trigger_and_its_grace_period()
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
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var service = new TriggerRegistrationService(new RecordingWorkflowDefinitionProvider(definition), triggerRegistrationProvider, new RecordingScheduledFireProvider(), new FixedClock());

        await service.OnPublishedAsync(42, WorkspaceRef, CancellationToken.None);

        TriggerRegistrationRow row = Assert.Single(triggerRegistrationProvider.Rows);
        Assert.Equal("presence", row.TriggerKind);
        Assert.StartsWith(ClientPresenceTriggerKey.Prefix(clientRef, ClientPresenceState.Offline), row.TriggerKey);
        Assert.EndsWith($":300:{row.WorkflowRefId}:{nodeId}", row.TriggerKey);
        Assert.True(ClientPresenceTriggerKey.TryGetForSeconds(row.TriggerKey, out int forSeconds));
        Assert.Equal(300, forSeconds);
        Assert.Equal(nameof(WorkflowConcurrencyPolicy.Queue), row.ConcurrencyPolicy);
    }

    private sealed class RecordingWorkflowDefinitionProvider(WorkflowDefinition definition) : IWorkflowDefinitionProvider
    {
        public Task<IReadOnlyCollection<WorkflowVersionRow>> GetVersionsAsync(Guid refId, int workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDeletion?> DeleteAsync(Guid refId, int workspaceId, int deletedBy, CancellationToken ct) => throw new NotSupportedException();
        public Task<Wbskt.Models.IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct) => Task.FromResult<Wbskt.Models.IPagedList<WorkflowDefinitionRow>>(new Wbskt.Models.PagedList<WorkflowDefinitionRow>(Array.Empty<WorkflowDefinitionRow>(), 0));
        public WorkflowDefinitionRow? Row { get; set; }
        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteUnreferencedAsync(int id, CancellationToken ct) => Task.FromResult(true);
        public Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct) => Task.CompletedTask;

        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
        {
            return Task.FromResult(new WorkflowDefinitionRow
            {
                Id = id,
                RefId = definition.WorkflowRefId,
                Version = definition.Version,
                WorkspaceId = definition.WorkspaceId,
                Name = definition.Name,
                Description = definition.Description,
                IsEnabled = definition.IsEnabled,
                DefinitionJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = definition.PublishedBy,
                CreatedAt = definition.CreatedAt
            });
        }
    }

    private sealed class RecordingTriggerRegistrationProvider : ITriggerRegistrationProvider
    {
        public List<TriggerRegistrationRow> Rows { get; } = [];
        public List<int> DeletedWorkflowDefinitionIds { get; } = [];

        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct)
        {
            Rows.Add(row);
            return Task.FromResult(row);
        }

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
        {
            DeletedWorkflowDefinitionIds.Add(workflowDefinitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScheduledFireProvider : IScheduledFireProvider
    {
        public List<(Guid TriggerNodeId, int WorkflowDefinitionId, Guid WorkflowRefId, string Cron, DateTime NextFireAt)> InsertCalls { get; } = [];
        public List<int> DeletedWorkflowDefinitionIds { get; } = [];

        public Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct)
        {
            InsertCalls.Add((triggerNodeId, workflowDefinitionId, workflowRefId, cronOrInterval, nextFireAt));
            return Task.FromResult(new ScheduledFireRow
            {
                Id = InsertCalls.Count,
                TriggerNodeId = triggerNodeId,
                WorkflowDefinitionId = workflowDefinitionId,
                WorkflowRefId = workflowRefId,
                CronOrInterval = cronOrInterval,
                NextFireAt = nextFireAt,
                LeasedUntil = null,
                CreatedAt = nextFireAt
            });
        }

        public Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
        {
            DeletedWorkflowDefinitionIds.Add(workflowDefinitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}
