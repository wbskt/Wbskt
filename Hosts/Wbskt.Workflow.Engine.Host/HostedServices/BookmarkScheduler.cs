using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>Resumes branches whose timer or wait timeout is due.</summary>
public sealed class BookmarkScheduler(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    IRunDispatcher runDispatcher,
    ILogger<BookmarkScheduler> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private readonly int _batchSize = options.Value.BookmarkLeaseBatchSize;

    protected override string Name => "Bookmark scheduler";

    protected override TimeSpan Interval { get; } = options.Value.BookmarkPollInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var branchProvider = services.GetRequiredService<IBranchProvider>();
        var bookmarkProvider = services.GetRequiredService<IBookmarkProvider>();
        // Claim-by-delete: rows returned here are already removed from the table, no separate
        // delete step needed (and no lease/host-id bookkeeping - the DELETE itself is the claim).
        IReadOnlyCollection<Abstraction.Entities.BookmarkRow> claimedBookmarks =
            await bookmarkProvider.ClaimDueAsync(clock.GetUtcNow().UtcDateTime, _batchSize, ct);

        if (claimedBookmarks.Count > 0)
        {
            Logger.LogInformation("Claimed {Count} due bookmarks for processing", claimedBookmarks.Count);
        }

        foreach (var bookmark in claimedBookmarks)
        {
            try
            {
                var branch = await branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);

                // A TtlPort on a non-timer bookmark means the primary wait (signal/http/child) timed
                // out via its TTL rather than being matched - take the timeout edge. A pure timer
                // wait (a Delay node) has no take-port; it just resumes at its own node.
                bool isTtlFiring = !string.IsNullOrEmpty(bookmark.TtlPort) && !string.Equals(bookmark.WakeConditionKind, "timer", StringComparison.OrdinalIgnoreCase);
                if (isTtlFiring)
                {
                    branch = branch with { PendingTakePort = bookmark.TtlPort };
                    await branchProvider.UpsertAsync(branch, ct);
                }

                Logger.LogInformation("Resuming bookmark {BookmarkId} (Timer Due) for run {RunId} on branch {BranchId}", bookmark.Id, bookmark.RunId, branch.Id);
                await runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to process claimed bookmark {BookmarkId} for run {RunId}", bookmark.Id, bookmark.RunId);
            }
        }
    }
}
