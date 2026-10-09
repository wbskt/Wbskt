using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>Publishes <see cref="SharedVariableSetByRunEvent"/>, with the run as its actor.</summary>
public sealed class VariableWritePublisher(IEventBus eventBus, ILogger<VariableWritePublisher> logger) : IVariableWritePublisher
{
    public async Task PublishAsync(VariableWrite write, CancellationToken ct)
    {
        var @event = new SharedVariableSetByRunEvent(write.WorkflowRefId, write.WorkflowId, write.WorkspaceId, write.Name, write.Operation, SharedVariableValues.Cut(write.ValueJson))
        {
            ActorSource = EventSource.Workflow,
            ActorWorkflowRefId = write.WorkflowRefId,
            ActorRunRefId = write.RunRefId
        };

        try
        {
            await eventBus.PublishAsync(@event, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not announce run {RunRefId}'s write to shared variable '{Name}'; the value is stored.", write.RunRefId, write.Name);
        }
    }
}
