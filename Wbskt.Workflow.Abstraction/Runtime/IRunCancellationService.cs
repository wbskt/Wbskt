namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCancellationService
{
    Task RequestCancellationAsync(long runId, string reason, CancellationToken ct);
}
