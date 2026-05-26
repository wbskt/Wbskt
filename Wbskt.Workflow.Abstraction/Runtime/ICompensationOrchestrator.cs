namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ICompensationOrchestrator
{
    Task RunAsync(long runId, long branchId, CancellationToken ct);
}
