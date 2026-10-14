namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCancellationService
{
    /// <summary>
    /// Moves a non-terminal run into <c>Cancelling</c> and applies the cancellation on this host:
    /// bookmarks deleted, waiting branches cancelled, the run finalized if nothing is left running.
    /// Idempotent - a run already in <c>Cancelling</c> gets the cleanup again.
    /// </summary>
    /// <returns><c>false</c> only when the run cannot be cancelled at all, i.e. it is already terminal.</returns>
    Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct);

    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);

    CancellationToken GetToken(long runId) => CancellationToken.None;
    void CancelCts(long runId) {}
}
