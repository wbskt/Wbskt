namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IPendingTriggerEventDrainer
{
    Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        => Task.CompletedTask;
}
