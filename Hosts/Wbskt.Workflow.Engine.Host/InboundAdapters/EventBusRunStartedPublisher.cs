using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Publishes a <see cref="WorkflowRunStartedEvent"/> onto the bus when a run starts, so the run
/// appears in the central Events Log. WorkspaceId is resolved from the (in-memory cached) workflow
/// definition; the detailed per-node trace stays in HistoryEvents.
/// </summary>
public sealed class EventBusRunStartedPublisher(IWorkflowDefinitionCache definitionCache, IEventBus eventBus) : IRunStartedPublisher
{
    public async Task PublishAsync(RunRow run, CancellationToken ct)
    {
        WorkflowDefinition definition = await definitionCache.GetAsync(run.WorkflowDefinitionId, ct);
        await eventBus.PublishAsync(
            new WorkflowRunStartedEvent(run.WorkflowRefId, run.WorkflowDefinitionId, run.RefId, definition.WorkspaceId),
            ct);
    }
}
