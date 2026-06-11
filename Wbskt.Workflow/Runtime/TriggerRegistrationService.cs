using System.Text.Json;
using Cronos;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerRegistrationService : ITriggerRegistrationService
{
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;
    private readonly IScheduledFireProvider _scheduledFireProvider;
    private readonly IClock _clock;

    public TriggerRegistrationService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        IScheduledFireProvider scheduledFireProvider,
        IClock clock)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationProvider = triggerRegistrationProvider;
        _scheduledFireProvider = scheduledFireProvider;
        _clock = clock;
    }

    public async Task OnPublishedAsync(int workflowDefinitionId, CancellationToken ct)
    {
        WorkflowDefinitionRow definitionRow = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(definitionRow.DefinitionJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} could not be deserialized.");

        foreach (BaseNode node in definition.Nodes)
        {
            TriggerRegistrationRow? registration = node switch
            {
                DeviceTriggerNode deviceTrigger => CreateRegistration(definitionRow, deviceTrigger.NodeId, "device", $"device:{deviceTrigger.Config.DeviceRef}:{deviceTrigger.Config.Event}", deviceTrigger.Config.ConcurrencyPolicy.ToString(), deviceTrigger.Config.CorrelationKey),
                WebhookTriggerNode webhookTrigger => CreateRegistration(definitionRow, webhookTrigger.NodeId, "webhook", $"webhook:{webhookTrigger.Config.Path}", webhookTrigger.Config.ConcurrencyPolicy.ToString(), webhookTrigger.Config.CorrelationKey),
                ManualTriggerNode manualTrigger => CreateRegistration(definitionRow, manualTrigger.NodeId, "manual", $"manual:{definitionRow.RefId}", WorkflowConcurrencyPolicy.AllowParallel.ToString(), null),
                ScheduleTriggerNode scheduleTrigger => await CreateScheduleRegistrationAsync(definitionRow, scheduleTrigger, ct),
                _ => null
            };

            if (registration is not null)
            {
                await _triggerRegistrationProvider.InsertAsync(registration, ct);
            }
        }
    }

    public async Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await _triggerRegistrationProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);
        await _scheduledFireProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);
    }

    private async Task<TriggerRegistrationRow> CreateScheduleRegistrationAsync(WorkflowDefinitionRow definitionRow, ScheduleTriggerNode scheduleTrigger, CancellationToken ct)
    {
        CronExpression cron = CronExpression.Parse(scheduleTrigger.Config.Cron);
        DateTime nextFireAt = cron.GetNextOccurrence(_clock.UtcNow, TimeZoneInfo.Utc)
            ?? throw new InvalidOperationException($"Schedule trigger '{scheduleTrigger.NodeId}' did not produce a next fire time.");
        ScheduledFireRow scheduledFire = await _scheduledFireProvider.InsertAsync(scheduleTrigger.NodeId, definitionRow.Id, definitionRow.RefId, scheduleTrigger.Config.Cron, nextFireAt, ct);

        return CreateRegistration(definitionRow, scheduleTrigger.NodeId, "schedule", $"schedule:{scheduledFire.Id}", WorkflowConcurrencyPolicy.AllowParallel.ToString(), null);
    }

    private static TriggerRegistrationRow CreateRegistration(WorkflowDefinitionRow definitionRow, Guid triggerNodeId, string triggerKind, string triggerKey, string concurrencyPolicy, string? correlationExpression)
    {
        return new TriggerRegistrationRow
        {
            Id = 0,
            WorkflowDefinitionId = definitionRow.Id,
            WorkflowRefId = definitionRow.RefId,
            WorkflowVersion = definitionRow.Version,
            TriggerNodeId = triggerNodeId,
            TriggerKind = triggerKind,
            TriggerKey = triggerKey,
            CorrelationExpression = correlationExpression,
            ConcurrencyPolicy = concurrencyPolicy,
            FilterExpression = null,
            CreatedAt = definitionRow.CreatedAt
        };
    }
}
