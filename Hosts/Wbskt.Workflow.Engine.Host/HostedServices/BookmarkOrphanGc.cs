using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Reconciles bookmarks with the branches they wake, in both directions: deletes bookmarks whose run is
/// over, and fails Waiting branches that have no bookmark left to wake them.
/// </summary>
public sealed class BookmarkOrphanGc(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<BookmarkOrphanGc> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private const int StrandedBatchSize = 100;
    private readonly TimeSpan _strandedThreshold = options.Value.StrandedBranchThreshold;

    // Branches seen stranded on the previous pass, by id, with the row version they had then. A wake
    // deletes its bookmark (the claim) before the woken branch is written, so for that moment a branch
    // looks stranded however old it is; the grace period cannot tell the two apart because the claim
    // does not touch the branch. Failing only a branch seen stranded, unchanged, on two passes in a row
    // leaves a whole interval for a wake to reach it. Touched by one tick at a time.
    private Dictionary<long, byte[]> _suspects = new();

    protected override string Name => "Bookmark orphan GC";

    protected override TimeSpan Interval { get; } = options.Value.BookmarkOrphanGcInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var bookmarkProvider = services.GetRequiredService<IBookmarkProvider>();
        int deletedCount = await bookmarkProvider.DeleteOrphansAsync(ct);
        if (deletedCount > 0)
        {
            Logger.LogInformation("Orphan GC removed {Count} orphaned bookmarks", deletedCount);
        }

        await FailStrandedBranchesAsync(services, ct);
    }

    private async Task FailStrandedBranchesAsync(IServiceProvider services, CancellationToken ct)
    {
        var branchProvider = services.GetRequiredService<IBranchProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _strandedThreshold;
        IReadOnlyCollection<BranchRow> stranded = await branchProvider.GetWaitingWithoutBookmarkAsync(cutoffUtc, StrandedBatchSize, ct);

        Dictionary<long, byte[]> previous = _suspects;
        var suspects = new Dictionary<long, byte[]>();
        IStrandedBranchFailer? failer = null;
        int failedCount = 0;
        foreach (BranchRow branch in stranded)
        {
            if (!previous.TryGetValue(branch.Id, out byte[]? seenVersion) || !seenVersion.AsSpan().SequenceEqual(branch.RowVersion))
            {
                suspects[branch.Id] = branch.RowVersion;
                continue;
            }

            try
            {
                failer ??= services.GetRequiredService<IStrandedBranchFailer>();
                if (await failer.FailAsync(branch, ct))
                {
                    failedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One branch that cannot be failed must not keep the rest of the batch stranded. It is
                // still stranded, so it stays a suspect and is retried on the next pass.
                suspects[branch.Id] = branch.RowVersion;
                Logger.LogError(ex, "Orphan GC could not fail stranded branch {BranchId} of run {RunId}", branch.Id, branch.RunId);
            }
        }

        _suspects = suspects;
        if (failedCount > 0)
        {
            Logger.LogWarning("Orphan GC failed {Count} branches stranded Waiting with no bookmark", failedCount);
        }
    }
}
