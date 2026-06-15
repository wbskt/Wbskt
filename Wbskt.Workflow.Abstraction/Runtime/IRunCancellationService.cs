using System.Threading;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCancellationService
{
    Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct);
    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);
    CancellationToken GetToken(long runId) => CancellationToken.None;
    void CancelCts(long runId) {}
    void RemoveCts(long runId) {}
}
