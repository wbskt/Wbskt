using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerRegistrationServiceTests
{
    [Fact]
    public async Task OnPublished_inserts_one_registration_per_trigger_node()
    {
        // Arrange
        var workflowDefinitionProvider = new RecordingWorkflowDefinitionProvider(CreateDefinition());
        var triggerRegistrationProvider = new RecordingTriggerRegistrationProvider();
        var service = new TriggerRegistrationService(workflowDefinitionProvider, triggerRegistrationProvider, new RecordingScheduledFireProvider(), new FixedClock());

        // Act
        await service.OnPublishedAsync(42, CancellationToken.None);

        // Assert
        Assert.Equal(4, triggerRegistrationProvider.Rows.Count);
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "device" && row.TriggerKey == "device:device-serial-1:telemetry");
        Assert.Contains(triggerRegistrationProvider.Rows, row => row.TriggerKind == "webhook" && row.TriggerKey == "webhook:/hooks/intake");
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
        await service.OnPublishedAsync(42, CancellationToken.None);

        // Assert
        Assert.Single(scheduledFireProvider.InsertCalls);
        Assert.Equal("*/5 * * * *", scheduledFireProvider.InsertCalls.Single().Cron);
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
                new DeviceTriggerNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "device", Ports = [], Config = new DeviceTriggerConfig("device-serial-1", "telemetry", null, WorkflowConcurrencyPolicy.Queue) },
                new ScheduleTriggerNode { NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "schedule", Ports = [], Config = new ScheduleTriggerConfig("*/5 * * * *") },
                new WebhookTriggerNode { NodeId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "webhook", Ports = [], Config = new WebhookTriggerConfig("/hooks/intake", "POST", null, WorkflowConcurrencyPolicy.DropIfRunning) },
                new ManualTriggerNode { NodeId = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "manual", Ports = [], Config = new ManualTriggerConfig("manual") }
            ],
            [],
            [],
            new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            7);
    }

    private sealed class RecordingWorkflowDefinitionProvider(WorkflowDefinition definition) : IWorkflowDefinitionProvider
    {
        public Task<(int TotalCount, IReadOnlyCollection<WorkflowDefinitionRow> Items)> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct) => Task.FromResult<(int, IReadOnlyCollection<WorkflowDefinitionRow>)>((0, Array.Empty<WorkflowDefinitionRow>()));
        public WorkflowDefinitionRow? Row { get; set; }
        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task DeprecateAsync(int id, CancellationToken ct) => throw new NotSupportedException();

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
