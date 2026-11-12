using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunRecoveryService : IEngineStartupTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<RunRecoveryService> _logger;
    private readonly TimeProvider _clock;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _readyTcs.Task;

    public RunRecoveryService(
        IServiceScopeFactory scopeFactory,
        IRunDispatcher runDispatcher,
        ILogger<RunRecoveryService> logger,
        TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _runDispatcher = runDispatcher;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The most woken-but-lost branches recovered in one start. More than this is a sign of something
    /// else wrong; the rest are left to the orphan GC.
    /// </summary>
    internal const int WakeRecoveryLimit = 10_000;

    // Recovery only ever runs on a new leader (see EngineLeadershipCoordinator) - the previous
    // leader is either dead or self-fenced by LeadershipState, so no other instance can be
    // concurrently dispatching these same branches. Not an IHostedService: EngineLeadershipCoordinator
    // calls this directly, only once this instance becomes leader (see Program.cs).
    public async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var branchProvider = scope.ServiceProvider.GetRequiredService<IBranchProvider>();
            var runProvider = scope.ServiceProvider.GetService<IRunProvider>();
            var runCancellationService = scope.ServiceProvider.GetService<IRunCancellationService>();

            IReadOnlyCollection<BranchRow> branches = await branchProvider.GetRunningBranchesAsync(ct);
            
            var runGroups = branches.GroupBy(b => b.RunId);
            var reappliedRunIds = new HashSet<long>();

            foreach (var group in runGroups)
            {
                long runId = group.Key;
                reappliedRunIds.Add(runId);
                
                if (runProvider is not null && runCancellationService is not null)
                {
                    try
                    {
                        RunRow run = await runProvider.GetByIdAsync(runId, ct);
                        if (string.Equals(run.Status, RunStatus.Cancelling, StringComparison.Ordinal))
                        {
                            // Cancelling CTS alone left the run's bookmarks and waiting branches behind:
                            // the branches we are about to re-dispatch unwind, but a parked sibling keeps
                            // ActiveBranchCount above zero forever, and the reaper skips any run holding a
                            // bookmark - so the run could never reach a terminal status. Re-applying the
                            // cancellation is idempotent on an already-'Cancelling' run and does that cleanup.
                            await runCancellationService.RequestCancellationAsync(runId, run.CancellationReason ?? "Cancellation re-applied during recovery", ct);
                        }
                        else if (string.Equals(run.Status, RunStatus.Failing, StringComparison.Ordinal))
                        {
                            // Failing is not cancelled, so it must not be moved to 'Cancelling'. But the
                            // failure stops the run's work just as a cancel does, and the previous leader may
                            // have died before finishing that: re-applying the failure is idempotent on an
                            // already-'Failing' run and deletes the bookmarks and cancels the waiting
                            // branches, so a parked sibling cannot keep the run alive after a failover.
                            await runCancellationService.FailRunAsync(runId, "Run failure re-applied during recovery", ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to inspect run {RunId} during startup recovery.", runId);
                    }
                }

                foreach (var branch in group)
                {
                    // The branch loop continues an interrupted branch from its persisted state exactly as
                    // it does a woken one; the reason differs only so the history says it was recovered.
                    await _runDispatcher.DispatchAsync(new BranchExecutionRequest(branch.RunId, branch.Id, BranchExecutionReason.Recovered), ct);
                }
            }

            if (runProvider is not null && runCancellationService is not null)
            {
                await ReapplyStopsAsync(runProvider, runCancellationService, reappliedRunIds, ct);
            }

            int woken = await RecoverLostWakesAsync(branchProvider, ct);

            _logger.LogInformation("Recovered {Count} running branches and {Woken} woken ones.", branches.Count, woken);
            _readyTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
            throw;
        }
    }

    /// <summary>
    /// Resumes every Waiting branch of a running run that has no bookmark: one whose wake (a timer, a
    /// signal, a command ack, a retry back-off) claimed its bookmark by deleting it and was then lost with
    /// the previous leader's in-memory channel. Each parking executor resumes from its persisted state and
    /// parks again when the wake was not its own, so a branch stranded mid-park is safe to resume too.
    /// Runs after <see cref="ReapplyStopsAsync"/>, which has already ended the waiting branches of runs
    /// that are stopping, so only running runs' branches are left to wake.
    /// </summary>
    private async Task<int> RecoverLostWakesAsync(IBranchProvider branchProvider, CancellationToken ct)
    {
        // No cutoff: this is a new leader, so no other instance is parking or waking a branch right now.
        DateTime now = _clock.GetUtcNow().UtcDateTime.AddSeconds(1);
        IReadOnlyCollection<BranchRow> waiting = await branchProvider.GetWaitingWithoutBookmarkAsync(now, WakeRecoveryLimit, ct);
        foreach (BranchRow branch in waiting)
        {
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(branch.RunId, branch.Id, BranchExecutionReason.Recovered), ct);
        }

        if (waiting.Count >= WakeRecoveryLimit)
        {
            _logger.LogWarning("Recovery found at least {Limit} Waiting branches with no bookmark and resumed only those; the orphan GC handles the rest.", WakeRecoveryLimit);
        }

        return waiting.Count;
    }

    /// <summary>
    /// Re-applies the stop of every run left in 'Failing' or 'Cancelling' that the branch pass above did
    /// not visit. That pass only finds runs through an Active branch, so a run whose
    /// stop was cut short by the crash and whose remaining branches were all Waiting was never visited:
    /// its parked siblings kept ActiveBranchCount above zero, and the reaper skips runs with Waiting
    /// branches, so it stayed Failing or Cancelling for good. Both stops are idempotent on a run already
    /// in their status.
    /// </summary>
    private async Task ReapplyStopsAsync(IRunProvider runProvider, IRunCancellationService runCancellationService, HashSet<long> alreadyReapplied, CancellationToken ct)
    {
        foreach (string status in new[] { RunStatus.Failing, RunStatus.Cancelling })
        {
            IReadOnlyCollection<RunRow> runs;
            try
            {
                runs = await runProvider.GetAllByStatusAsync(status, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read {Status} runs during startup recovery.", status);
                continue;
            }

            foreach (RunRow run in runs)
            {
                if (!alreadyReapplied.Add(run.Id))
                {
                    continue;
                }

                try
                {
                    if (string.Equals(status, RunStatus.Cancelling, StringComparison.Ordinal))
                    {
                        await runCancellationService.RequestCancellationAsync(run.Id, run.CancellationReason ?? "Cancellation re-applied during recovery", ct);
                    }
                    else
                    {
                        await runCancellationService.FailRunAsync(run.Id, "Run failure re-applied during recovery", ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to re-apply the stop of {Status} run {RunId} during startup recovery.", status, run.Id);
                }
            }
        }
    }
}
