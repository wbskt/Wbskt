using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Drops what the engine cached of a workflow when the management host announces that its definitions
/// or trigger registrations changed: the definitions named in the event, and every cached trigger
/// registration lookup (see <see cref="ITriggerRegistrationCache"/> for why every one).
/// </summary>
/// <remarks>
/// Picked up by the engine host's consumer scan, so it gets a shared, durable queue like the engine's
/// other consumers. Only the leader runs a bus, and only the leader dispatches, so the one instance that
/// caches is the one that hears; a standby that takes over starts with empty caches (and an event left
/// on the queue meanwhile only clears them again). Clearing twice is harmless, so a redelivery is too.
/// </remarks>
public sealed class WorkflowDefinitionChangedConsumer(
    IWorkflowDefinitionCache definitionCache,
    ITriggerRegistrationCache registrationCache,
    ILogger<WorkflowDefinitionChangedConsumer>? logger = null) : IConsumer<WorkflowDefinitionChangedEvent>
{
    public Task Consume(ConsumeContext<WorkflowDefinitionChangedEvent> context)
    {
        WorkflowDefinitionChangedEvent changed = context.Message;

        // Registrations first: they decide whether a message starts anything at all.
        registrationCache.Clear();

        foreach (int definitionId in changed.DefinitionIds ?? [])
        {
            definitionCache.Invalidate(definitionId);
        }

        logger?.LogDebug(
            "Workflow {WorkflowRefId} in workspace {WorkspaceId} changed; dropped {DefinitionCount} cached definition(s) and the cached trigger registrations.",
            changed.WorkflowRefId, changed.WorkspaceId, changed.DefinitionIds?.Count ?? 0);
        return Task.CompletedTask;
    }
}
