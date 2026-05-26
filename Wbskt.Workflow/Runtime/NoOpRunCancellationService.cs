using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class NoOpRunCancellationService : IRunCancellationService
{
    public Task RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        _ = runId;
        _ = reason;
        _ = ct;
        return Task.CompletedTask;
    }
}
