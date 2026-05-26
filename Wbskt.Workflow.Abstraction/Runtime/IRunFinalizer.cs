namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunFinalizer
{
    Task FinalizeAsync(long runId, CancellationToken ct);
}
