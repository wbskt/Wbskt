using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunCancellationService : IRunCancellationService
{
    private readonly IRunProvider _runProvider;
    private readonly IRunJournal _journal;
    private readonly TimeProvider _clock;
    private readonly IBranchProvider _branchProvider;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IBranchTerminator _branchTerminator;

    // Singleton, not per-instance: this service is Scoped and the pump runs each branch in its own
    // scope, so a per-instance registry meant the branch loop watched one token source while every
    // canceller cancelled another. See RunCancellationTokenRegistry.
    private readonly RunCancellationTokenRegistry _tokenRegistry;

    public RunCancellationService(
        IRunProvider runProvider,
        IRunJournal journal,
        TimeProvider clock,
        IBranchProvider branchProvider,
        IBookmarkProvider bookmarkProvider,
        IBranchTerminator branchTerminator,
        RunCancellationTokenRegistry tokenRegistry)
    {
        _tokenRegistry = tokenRegistry;
        _runProvider = runProvider;
        _journal = journal;
        _clock = clock;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
        _branchTerminator = branchTerminator;
    }

    public CancellationToken GetToken(long runId)
    {
        return _tokenRegistry.GetToken(runId);
    }

    // Public for the interface's sake; cancels and failures go through StopRunWorkAsync, which calls it.
    public void CancelCts(long runId)
    {
        _tokenRegistry.Cancel(runId);
    }

    public async Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        // A cancel applies to any non-terminal run, not just a 'Running' one. A run whose first branch
        // already failed sits in 'Failing' while its siblings keep going - refusing to cancel that was
        // silent and inexplicable from the outside. (A parked run is still 'Running'; only its branch is
        // 'Waiting'.) Single status write (2.3): the reason/timestamp ride along with the transition
        // itself, so there's no follow-up UpdateStatusAsync call.
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, RunStatus.Running, RunStatus.Cancelling, _clock.GetUtcNow().UtcDateTime, reason, ct)
            || await _runProvider.TransitionStatusAsync(runId, RunStatus.Failing, RunStatus.Cancelling, _clock.GetUtcNow().UtcDateTime, reason, ct);

        if (!transitioned)
        {
            // Not transitioning means either "already Cancelling" or "terminal". The first still gets the
            // cleanup below: a redelivered cancel command, or one sent while the run was already
            // cancelling, must not leave bookmarks or a waiting branch behind.
            string currentStatus = (await _runProvider.GetByIdAsync(runId, ct)).Status;
            if (!string.Equals(currentStatus, RunStatus.Cancelling, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else
        {
            await _journal.AppendAsync(
                new RunJournalEntry(checked((int)runId), HistoryEventKind.RunCancellationRequested) { Payload = new { Reason = reason } },
                ct);
        }

        await StopRunWorkAsync(runId, ct);
        return true;
    }

    public async Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct)
    {
        // Failing, not Cancelling: a run that failed by its own rule must finalize as Failed. It used
        // to be routed through a cancel, which moved it on to 'Cancelling' and so it finalized as
        // Cancelled - in the console that read as if someone had pressed cancel. The status write
        // carries no reason, because the run row's reason column is the cancellation reason the
        // finalizer reads (OUT_OF_CREDITS); the failure's reason lives in the journal entry instead.
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, RunStatus.Running, RunStatus.Failing, null, null, ct);

        if (!transitioned)
        {
            // Already 'Failing' (a second branch failing by the same rule, or recovery on a new
            // leader) still gets the cleanup below, like the already-Cancelling path of a cancel.
            // Anything else - 'Cancelling' or terminal - is already being stopped, or is over.
            string currentStatus = (await _runProvider.GetByIdAsync(runId, ct)).Status;
            if (!string.Equals(currentStatus, RunStatus.Failing, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else
        {
            await _journal.AppendAsync(
                new RunJournalEntry(checked((int)runId), HistoryEventKind.RunFailureRequested) { Payload = new { Reason = reason } },
                ct);
        }

        // Failing the run used to only move its status: a sibling parked on a signal kept the run in
        // 'Failing' until the signal came or its TTL ran out, so "fail fast" was not fast at all.
        await StopRunWorkAsync(runId, ct);

        return true;
    }

    /// <summary>
    /// The half of cancelling or failing a run that stops its work on this host. Every step is
    /// idempotent, which is what lets a repeated cancel or failure fall through to it safely.
    /// </summary>
    private async Task StopRunWorkAsync(long runId, CancellationToken ct)
    {
        // Cancel the token before the cleanup, so a branch running on this host stops mid-node.
        CancelCts(runId);

        // Mass-delete bookmarks for this run.
        await _bookmarkProvider.DeleteAllByRunIdAsync(checked((int)runId), ct);

        int cancelledBranches = await _branchProvider.CancelWaitingBranchesAsync(checked((int)runId), ct);

        // A run with nothing left running has no branch loop to carry it to a terminal status, so
        // without this it would sit in 'Cancelling' or 'Failing' until the 30-minute reaper swept it.
        // Releasing zero branches, as on a repeated cancel, still finalizes a run that is already idle.
        await ReleaseCancelledBranchesAsync(runId, cancelledBranches, ct);
    }

    private async Task ReleaseCancelledBranchesAsync(long runId, int cancelledBranches, CancellationToken ct)
    {
        // Through the terminator, not a bare SetTerminalAsync (2.3): that would skip the pending-trigger
        // drain, the run-completed publish and the sub-workflow completion hook, stranding parents.
        try
        {
            await _branchTerminator.ReleaseAsync(checked((int)runId), cancelledBranches, ct);
        }
        catch (Exception)
        {
            // The cancel itself succeeded; a run left in 'Cancelling' is picked up by the branch loop
            // when its last branch unwinds, or by the reaper. Failing the caller's cancel over this
            // would report "not cancelled" for a run that is, in fact, cancelling.
        }
    }

    public async Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
    {
        // A cancel taken on this host is answered from the token registry, so the branch loop's check
        // after an interrupted executor needs no database. Otherwise read the run row every time: a
        // cancel taken by another host only shows up there, and a cached answer kept this host running
        // a cancelled run until the entry expired.
        if (_tokenRegistry.IsCancelled(runId))
        {
            return true;
        }

        // 'Failing' counts too: a run only gets there through FailRunAsync, which stops its work, so a
        // branch that did not see the token (another host's, or one dispatched after a restart) must
        // stop as well rather than keep a failed run alive.
        string status = (await _runProvider.GetByIdAsync(runId, ct)).Status;
        return string.Equals(status, RunStatus.Cancelling, StringComparison.Ordinal)
            || string.Equals(status, RunStatus.Cancelled, StringComparison.Ordinal)
            || string.Equals(status, RunStatus.Failing, StringComparison.Ordinal);
    }
}
