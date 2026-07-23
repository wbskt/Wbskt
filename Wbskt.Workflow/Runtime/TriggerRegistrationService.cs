using System.Text.Json;
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

    public async Task OnPublishedAsync(int workflowDefinitionId, Guid workspaceRef, CancellationToken ct)
    {
        WorkflowDefinitionRow definitionRow = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(definitionRow.DefinitionJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} could not be deserialized.");

        foreach (BaseNode node in definition.Nodes)
        {
            TriggerRegistrationRow? registration = node switch
            {
                ClientTriggerNode clientTrigger => CreateRegistration(definitionRow, clientTrigger.NodeId, "client", $"client:{clientTrigger.Config.ClientRef}:{clientTrigger.Config.Type}", clientTrigger.Config.ConcurrencyPolicy.ToString(), clientTrigger.Config.CorrelationKey),
                // Webhook keys are workspace-scoped so an author-chosen path is unique per workspace
                // and the anonymous public callback for one workspace can never fire another's trigger.
                WebhookTriggerNode webhookTrigger => CreateRegistration(definitionRow, webhookTrigger.NodeId, "webhook", $"webhook:{workspaceRef}:{webhookTrigger.Config.Path}", webhookTrigger.Config.ConcurrencyPolicy.ToString(), webhookTrigger.Config.CorrelationKey),
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
        if (!CronParser.TryGetNextOccurrence(scheduleTrigger.Config.Cron, _clock.UtcNow, out DateTime nextFireAt))
        {
            throw new InvalidOperationException($"Schedule trigger '{scheduleTrigger.NodeId}' has an invalid cron expression '{scheduleTrigger.Config.Cron}'.");
        }
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
            
            // The field is designed as a pre-filtering mechanism for event-driven triggers (such as Webhooks or Device Telemetry):
            //
            // • Event Filtering: It holds a path expression or query (e.g. $.status = ='active' or $.value > 50  in JSONPath) that must be evaluated against the incoming trigger event payload.
            // • Avoid unnecessary runs: By checking this expression at the  TriggerDispatcher  layer, the system can determine whether to start a workflow run. If the incoming event payload does not satisfy the  FilterExpression , the event is discarded before a
            // run is initialized, saving compute resource costs.
            FilterExpression = null,
            CreatedAt = definitionRow.CreatedAt
        };
    }
}
