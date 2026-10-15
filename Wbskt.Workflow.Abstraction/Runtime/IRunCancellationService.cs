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

    /// <summary>
    /// Fails the run by its own rule (an <c>onFailure</c> of <c>FailRun</c>, a <c>control:failRun</c>
    /// node, or a fail-fast workflow): moves a <c>Running</c> run into <c>Failing</c> and stops the rest
    /// of its work the same way a cancel does - token cancelled, bookmarks deleted, waiting branches
    /// cancelled, the run finalized if nothing is left running. The run is never moved to
    /// <c>Cancelling</c>, so it finalizes as <c>Failed</c>. Idempotent - a run already in
    /// <c>Failing</c> gets the cleanup again.
    /// </summary>
    /// <returns><c>false</c> when the run is neither <c>Running</c> nor <c>Failing</c>, e.g. already
    /// cancelling or terminal; nothing is done then.</returns>
    Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct);

    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);

    CancellationToken GetToken(long runId) => CancellationToken.None;
    void CancelCts(long runId) {}
}
