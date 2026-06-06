using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Publishes a run-terminal milestone onto the bus when a run finalizes, so it appears in the
/// central Events Log. A failure terminal status (Failed or PartiallyFailed) is surfaced as a
/// high-criticality <see cref="WorkflowRunFailedEvent"/>; any other terminal status (Succeeded or
/// Cancelled) as an informational <see cref="WorkflowRunCompletedEvent"/>. WorkspaceId is resolved
/// from the (in-memory cached) workflow definition.
/// </summary>
public sealed class EventBusRunCompletedPublisher(IWorkflowDefinitionCache definitionCache, IEventBus eventBus) : IRunCompletedPublisher
{
    public async Task PublishAsync(RunRow run, string terminalStatus, CancellationToken ct)
    {
        WorkflowDefinition definition = await definitionCache.GetAsync(run.WorkflowDefinitionId, ct);

        if (IsFailure(terminalStatus))
        {
            await eventBus.PublishAsync(
                new WorkflowRunFailedEvent(run.WorkflowRefId, run.WorkflowDefinitionId, run.RefId, definition.WorkspaceId, terminalStatus),
                ct);
            return;
        }

        await eventBus.PublishAsync(
            new WorkflowRunCompletedEvent(run.WorkflowRefId, run.WorkflowDefinitionId, run.RefId, definition.WorkspaceId, terminalStatus),
            ct);
    }

    private static bool IsFailure(string terminalStatus)
    {
        return terminalStatus is "Failed" or "PartiallyFailed";
    }
}
