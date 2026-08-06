namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCancellationService
{
    /// <summary>
    /// Moves a non-terminal run into <c>Cancelling</c> and applies the cancellation on this host:
    /// bookmarks deleted, waiting branches cancelled, the run finalized if nothing is left running.
    /// Idempotent - a run another host already moved to <c>Cancelling</c> still gets the local work.
    /// </summary>
    /// <returns><c>false</c> only when the run cannot be cancelled at all, i.e. it is already terminal.</returns>
    Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct);

    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);

    /// <summary>
    /// Records host-locally that this run is being cancelled - cancels its token and makes
    /// <see cref="IsCancellationRequestedAsync"/> report it immediately rather than after the read-through
    /// cache expires. Cancellation state is cached per host, so a host learning of a cancel from the event
    /// bus must call this or it keeps serving a stale answer for the rest of the cache window.
    /// </summary>
    void MarkCancellationRequested(long runId) {}

    CancellationToken GetToken(long runId) => CancellationToken.None;
    void CancelCts(long runId) {}
    void RemoveCts(long runId) {}
}
