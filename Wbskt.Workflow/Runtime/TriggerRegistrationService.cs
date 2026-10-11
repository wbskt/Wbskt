using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerRegistrationService : ITriggerRegistrationService
{
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationProvider _triggerRegistrationProvider;
    private readonly IScheduledFireProvider _scheduledFireProvider;
    private readonly ITriggerChannelRegistry _channels;
    private readonly IClock _clock;

    public TriggerRegistrationService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationProvider triggerRegistrationProvider,
        IScheduledFireProvider scheduledFireProvider,
        ITriggerChannelRegistry channels,
        IClock clock)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationProvider = triggerRegistrationProvider;
        _scheduledFireProvider = scheduledFireProvider;
        _channels = channels;
        _clock = clock;
    }

    public async Task OnPublishedAsync(int workflowDefinitionId, Guid workspaceRef, CancellationToken ct)
    {
        WorkflowDefinitionRow definitionRow = await _workflowDefinitionProvider.GetByIdAsync(workflowDefinitionId, ct);
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(definitionRow.DefinitionJson, EngineJson.Options)
            ?? throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} could not be deserialized.");

        // Registering replaces whatever this version already had, so a retried or repeated
        // registration (a reinstate run twice) leaves one set of triggers, not two.
        await _triggerRegistrationProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);
        await _scheduledFireProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);

        var publication = new TriggerPublication(definitionRow, workspaceRef, _scheduledFireProvider, _clock);
        foreach (BaseNode node in definition.Nodes)
        {
            // At most one channel claims a node: the one its trigger kind (and, for a client trigger,
            // whether it holds) belongs to.
            foreach (ITriggerChannel channel in _channels.All)
            {
                TriggerRegistrationSpec? spec = await channel.RegisterAsync(node, publication, ct);
                if (spec is not null)
                {
                    await _triggerRegistrationProvider.InsertAsync(CreateRegistration(definitionRow, node.NodeId, channel.Kind, spec), ct);
                    break;
                }
            }
        }
    }

    public async Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await _triggerRegistrationProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);
        await _scheduledFireProvider.DeleteAllByWorkflowDefinitionIdAsync(workflowDefinitionId, ct);
    }

    private static TriggerRegistrationRow CreateRegistration(WorkflowDefinitionRow definitionRow, Guid triggerNodeId, string triggerKind, TriggerRegistrationSpec spec)
    {
        return new TriggerRegistrationRow
        {
            Id = 0,
            WorkflowDefinitionId = definitionRow.Id,
            WorkflowRefId = definitionRow.RefId,
            WorkflowVersion = definitionRow.Version,
            TriggerNodeId = triggerNodeId,
            TriggerKind = triggerKind,
            TriggerKey = spec.TriggerKey,
            CorrelationExpression = spec.CorrelationExpression,
            ConcurrencyPolicy = spec.ConcurrencyPolicy,

            // Copied onto the row rather than read from the definition at dispatch time: the dispatcher
            // matches registrations by key and would otherwise have to load and deserialize a whole
            // definition per candidate just to decide whether to discard the event.
            FilterExpression = spec.Filter is null ? null : JsonSerializer.Serialize(spec.Filter, EngineJson.Options),
            WebhookSecret = spec.WebhookSecret,
            CreatedAt = definitionRow.CreatedAt
        };
    }
}
