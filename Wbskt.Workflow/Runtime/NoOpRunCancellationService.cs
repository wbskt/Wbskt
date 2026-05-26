using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class NoOpRunCancellationService : IRunCancellationService
{
    public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        _ = runId;
        _ = reason;
        _ = ct;
        return Task.FromResult(false);
    }

    public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
    {
        _ = runId;
        _ = ct;
        return Task.FromResult(false);
    }
}
