using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class BranchTerminator(IRunCountersProvider runCountersProvider, Lazy<IRunFinalizer> runFinalizer) : IBranchTerminator
{
    // runFinalizer is Lazy because finishing a run goes back in through the front door: the finalizer
    // drains queued triggers and wakes a parent run through the inbound hub, the hub's dispatcher can
    // cancel a run (CancelExisting), and a cancel releases branches here. The cycle is real and by
    // design, so it is broken here, and the finalizer is resolved only when a run has nothing running.
    public async Task<int> ReleaseAsync(int runId, int count, CancellationToken ct)
    {
        // The clamped decrement: a release replayed after a crash must not drive the count below zero,
        // where the run would never again be seen as having nothing running.
        int remaining = await runCountersProvider.DecrementActiveBranchesAsync(runId, count, ct);
        if (remaining <= 0)
        {
            await runFinalizer.Value.FinalizeAsync(runId, ct);
        }

        return remaining;
    }
}
