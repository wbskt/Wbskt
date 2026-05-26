namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCancellationService
{
    Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct);
    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);
}
