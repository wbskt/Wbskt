using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Finalizes runs that are not terminal but have no work left: no bookmark and no Active or Waiting
/// branch (<c>Run_GetStuck</c>). Ending a branch, releasing its count and finalizing the run are separate
/// calls, so an engine that stops between them leaves such a run behind with nothing to finalize it.
/// Recovery sweeps them as soon as a new leader starts; the reaper sweeps the ones older than its
/// threshold. The status comes from the branches, as it would have on the normal path.
/// </summary>
public sealed class SettledRunSweeper(
    IRunProvider runProvider,
    IBranchProvider branchProvider,
    IRunCancellationService runCancellationService,
    IRunFinalizer runFinalizer)
{
    public const string ReaperTimeoutReason = "REAPER_TIMEOUT";

    /// <summary>
    /// Finalizes every settled run created before <paramref name="createdBeforeUtc"/>, a batch at a time,
    /// and answers how many it finalized.
    /// </summary>
    public async Task<int> FinalizeAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken ct)
    {
        var seen = new HashSet<long>();
        while (true)
        {
            IReadOnlyCollection<RunRow> runs = await runProvider.GetStuckRunsAsync(createdBeforeUtc, batchSize, ct);
            bool progressed = false;
            foreach (RunRow run in runs)
            {
                // A run finalized here leaves the next read; one that is still listed did not move, so
                // stop rather than read it again forever.
                if (!seen.Add(run.Id))
                {
                    continue;
                }

                progressed = true;
                await FinalizeOneAsync(run, ct);
            }

            if (runs.Count < batchSize || !progressed)
            {
                return seen.Count;
            }
        }
    }

    private async Task FinalizeOneAsync(RunRow run, CancellationToken ct)
    {
        // A Running run whose branches all ended Cancelled has no outcome to report: nothing in a
        // Running run cancels every branch. Stop it as timed out, so it does not finalize as Succeeded.
        if (string.Equals(run.Status, RunStatus.Running, StringComparison.Ordinal))
        {
            IReadOnlyCollection<BranchRow> branches = await branchProvider.GetAllByRunIdAsync(run.Id, ct);
            bool hasOutcome = branches.Any(branch =>
                string.Equals(branch.Status, BranchStatus.Completed, StringComparison.Ordinal)
                || string.Equals(branch.Status, BranchStatus.Failed, StringComparison.Ordinal));
            if (!hasOutcome)
            {
                await runCancellationService.RequestCancellationAsync(run.Id, ReaperTimeoutReason, ct);
            }
        }

        // Idempotent: a run the normal path finalizes at the same moment is transitioned only once.
        await runFinalizer.FinalizeAsync(run.Id, ct);
    }
}
