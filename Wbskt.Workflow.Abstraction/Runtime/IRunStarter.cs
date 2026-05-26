namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunStarter
{
    Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct);
}
