namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IPendingTriggerEventDrainer
{
    Task DrainAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct);
}
