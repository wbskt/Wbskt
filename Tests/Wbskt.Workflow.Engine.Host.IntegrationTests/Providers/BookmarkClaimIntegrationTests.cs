using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Phase 1.4 — Bookmark_ClaimDue / Bookmark_ClaimByRefId: claim-by-delete is atomic, so concurrent
/// callers must never observe the same bookmark row twice, whether claiming a due batch or racing
/// on a single RefId (the signal-vs-TTL race the single-row bookmark model relies on).
/// </summary>
[Collection("SqlEdge")]
public sealed class BookmarkClaimIntegrationTests(SqlEdgeFixture fixture)
{
    private async Task<int> SeedRunAsync()
    {
        var wdProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-BM-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        RunRow run = await runProvider.CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id, WorkflowRefId = wd.RefId, WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = "Running",
            StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return run.Id;
    }

    private static BookmarkRow BuildBookmark(int runId, DateTime? expiresAt) => new()
    {
        Id = 0, RefId = Guid.NewGuid(), RunId = runId, BranchRefId = Guid.NewGuid(),
        NodeId = Guid.NewGuid(), WakeConditionKind = "timer",
        MatchKey = string.Empty,
        WakeConditionJson = """{"kind":"timer","at":"2000-01-01T00:00:00Z"}""",
        ExpiresAt = expiresAt, TtlPort = null, CreatedAt = DateTime.UtcNow
    };

    [SkippableFact]
    public async Task ClaimDue_returns_and_removes_due_bookmarks()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await SeedRunAsync();
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        DateTime pastTime = DateTime.UtcNow.AddHours(-1);

        for (int i = 0; i < 5; i++)
        {
            await bookmarkProvider.CreateAsync(BuildBookmark(runId, pastTime.AddMinutes(i)), CancellationToken.None);
        }

        var due = await bookmarkProvider.ClaimDueAsync(DateTime.UtcNow, 10, CancellationToken.None);

        due.Should().HaveCountGreaterOrEqualTo(5);

        // Claim-by-delete: a second claim attempt must not see the same rows again.
        var second = await bookmarkProvider.ClaimDueAsync(DateTime.UtcNow, 10, CancellationToken.None);
        second.Select(b => b.RefId).Should().NotIntersectWith(due.Select(b => b.RefId));
    }

    [SkippableFact]
    public async Task ClaimDue_with_concurrent_callers_returns_disjoint_sets()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        const int totalBookmarks = 50;
        const int callerCount = 5;
        const int batchSize = 10;

        int runId = await SeedRunAsync();

        DateTime pastTime = DateTime.UtcNow.AddHours(-2);

        var seedProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        for (int i = 0; i < totalBookmarks; i++)
        {
            await seedProvider.CreateAsync(BuildBookmark(runId, pastTime.AddSeconds(i)), CancellationToken.None);
        }

        DateTime nowUtc = DateTime.UtcNow;
        var allResults = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callerCount),
            new ParallelOptions { MaxDegreeOfParallelism = callerCount },
            async (i, ct) =>
            {
                var p = ProviderFactory.Bookmark(fixture.ConnectionString);
                var batch = await p.ClaimDueAsync(nowUtc, batchSize, ct);
                foreach (var b in batch)
                {
                    allResults.Add(b.RefId);
                }
            });

        // Claim-by-delete guarantees disjoint sets — no RefId can be claimed twice.
        int distinctCount = allResults.Distinct().Count();
        distinctCount.Should().Be(allResults.Count,
            "claim-by-delete should give each caller distinct rows — no RefId duplicates");
    }

    [SkippableFact]
    public async Task ClaimDue_returns_empty_when_no_due_bookmarks()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        // Query far in the past — no bookmarks should be "due" relative to 1970
        DateTime longAgo = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var due = await bookmarkProvider.ClaimDueAsync(longAgo, 100, CancellationToken.None);

        due.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task TryClaimAsync_concurrent_calls_on_same_RefId_exactly_one_wins()
    {
        // The single-row bookmark model relies on this: a signal/http match and a TTL expiry
        // racing on the same bookmark row must never both succeed.
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await SeedRunAsync();
        var seedProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        BookmarkRow bookmark = await seedProvider.CreateAsync(BuildBookmark(runId, DateTime.UtcNow.AddMinutes(-1)), CancellationToken.None);

        const int callerCount = 10;
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callerCount),
            new ParallelOptions { MaxDegreeOfParallelism = callerCount },
            async (_, ct) =>
            {
                var p = ProviderFactory.Bookmark(fixture.ConnectionString);
                bool claimed = await p.TryClaimAsync(bookmark.RefId, ct);
                results.Add(claimed);
            });

        results.Count(claimed => claimed).Should().Be(1, "exactly one concurrent claimant should win the race");
        results.Count(claimed => !claimed).Should().Be(callerCount - 1);
    }

    private async Task<BranchRow> SeedWaitingBranchAsync(int runId)
    {
        return await ProviderFactory.Branch(fixture.ConnectionString).CreateAsync(new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = runId, ParentBranchId = null, ForkCohortId = null,
            NodeId = Guid.NewGuid(), Status = "Waiting", PendingTakePort = null, LocalJson = "{}",
            LastOutputJson = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, RowVersion = []
        }, CancellationToken.None);
    }

    private static BookmarkRow BookmarkFor(BranchRow branch, string kind, string? ttlPort) => new()
    {
        Id = 0, RefId = Guid.NewGuid(), RunId = branch.RunId, BranchRefId = branch.RefId,
        NodeId = branch.NodeId, WakeConditionKind = kind, MatchKey = $"it:{Guid.NewGuid():N}",
        WakeConditionJson = "{}", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), TtlPort = ttlPort, CreatedAt = DateTime.UtcNow
    };

    private async Task<IReadOnlyCollection<long>> WokenIdsAsync()
    {
        var branchProvider = ProviderFactory.Branch(fixture.ConnectionString);
        var ids = new List<long>();
        long afterId = 0;
        while (true)
        {
            IReadOnlyCollection<BranchRow> page = await branchProvider.GetWokenAsync(afterId, 500, CancellationToken.None);
            ids.AddRange(page.Select(branch => branch.Id));
            if (page.Count < 500)
            {
                return ids;
            }

            afterId = page.Max(branch => branch.Id);
        }
    }

    [SkippableFact]
    public async Task ClaimDue_stores_the_ttl_exit_and_marks_the_branch_woken_in_the_claim()
    {
        // wbskt/Wbskt#768: the scheduler used to write the TTL port after the claim, so a stop between
        // the two lost the timeout and the step ran its wait again; a timer's claim left nothing behind.
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await SeedRunAsync();
        BranchRow signalWait = await SeedWaitingBranchAsync(runId);
        BranchRow timerWait = await SeedWaitingBranchAsync(runId);
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        await bookmarkProvider.CreateAsync(BookmarkFor(signalWait, "signal", "timeout"), CancellationToken.None);
        await bookmarkProvider.CreateAsync(BookmarkFor(timerWait, "timer", null), CancellationToken.None);

        await bookmarkProvider.ClaimDueAsync(DateTime.UtcNow, 1000, CancellationToken.None);

        var branchProvider = ProviderFactory.Branch(fixture.ConnectionString);
        (await branchProvider.GetByIdAsync(signalWait.Id, CancellationToken.None)).PendingTakePort.Should().Be("timeout");
        (await branchProvider.GetByIdAsync(timerWait.Id, CancellationToken.None)).PendingTakePort.Should().BeNull();
        (await WokenIdsAsync()).Should().Contain([signalWait.Id, timerWait.Id]);

        // The next write that moves the branch clears the mark, so a later park is not taken for a wake.
        await branchProvider.UpdatePointerAsync(timerWait.Id, timerWait.NodeId, "Active", "{}", null, CancellationToken.None);
        (await WokenIdsAsync()).Should().NotContain(timerWait.Id);
    }

    [SkippableFact]
    public async Task TryClaimWithWake_stores_the_wake_only_for_the_winner()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await SeedRunAsync();
        BranchRow branch = await SeedWaitingBranchAsync(runId);
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        BookmarkRow bookmark = await bookmarkProvider.CreateAsync(BookmarkFor(branch, "signal", "timeout"), CancellationToken.None);

        (await bookmarkProvider.TryClaimWithWakeAsync(bookmark.RefId, """{"__wake":{"n":1}}""", CancellationToken.None)).Should().BeTrue();
        (await bookmarkProvider.TryClaimWithWakeAsync(bookmark.RefId, """{"__wake":{"n":2}}""", CancellationToken.None)).Should().BeFalse();

        BranchRow stored = await ProviderFactory.Branch(fixture.ConnectionString).GetByIdAsync(branch.Id, CancellationToken.None);
        stored.LocalJson.Should().Be("""{"__wake":{"n":1}}""");
        stored.PendingTakePort.Should().BeNull("the signal won the race, not the TTL");
        (await WokenIdsAsync()).Should().Contain(branch.Id);
    }
}
