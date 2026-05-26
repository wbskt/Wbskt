using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.4 — Bookmark_GetDue: verifies UPDLOCK + READPAST means concurrent callers
/// receive disjoint sets of due bookmarks.
/// </summary>
[Collection("SqlEdge")]
public sealed class BookmarkLeaseIntegrationTests(SqlEdgeFixture fixture, ITestOutputHelper output)
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

    private static BookmarkRow BuildBookmark(int runId, DateTime expiresAt) => new()
    {
        Id = 0, RefId = Guid.NewGuid(), RunId = runId, BranchRefId = Guid.NewGuid(),
        NodeId = Guid.NewGuid(), WakeConditionKind = "timer",
        MatchKey = expiresAt.ToString("O"),
        WakeConditionJson = """{"kind":"timer","at":"2000-01-01T00:00:00Z"}""",
        ExpiresAt = expiresAt, TtlPort = null, CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task LeaseDue_returns_due_bookmarks()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        int runId = await SeedRunAsync();
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        DateTime pastTime = DateTime.UtcNow.AddHours(-1);

        // Insert 5 due bookmarks
        for (int i = 0; i < 5; i++)
        {
            await bookmarkProvider.CreateAsync(BuildBookmark(runId, pastTime.AddMinutes(i)), CancellationToken.None);
        }

        var due = await bookmarkProvider.LeaseDueAsync(
            DateTime.UtcNow, 10, $"host-{Guid.NewGuid():N}", TimeSpan.FromMinutes(1), CancellationToken.None);

        due.Should().HaveCountGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task LeaseDue_with_concurrent_callers_returns_disjoint_sets()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        const int totalBookmarks = 50;
        const int callerCount = 5;
        const int batchSize = 10;

        int runId = await SeedRunAsync();
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        DateTime pastTime = DateTime.UtcNow.AddHours(-2);

        for (int i = 0; i < totalBookmarks; i++)
        {
            await bookmarkProvider.CreateAsync(
                BuildBookmark(runId, pastTime.AddSeconds(i)), CancellationToken.None);
        }

        DateTime nowUtc = DateTime.UtcNow;
        var allResults = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callerCount),
            new ParallelOptions { MaxDegreeOfParallelism = callerCount },
            async (i, ct) =>
            {
                var p = ProviderFactory.Bookmark(fixture.ConnectionString);
                var batch = await p.LeaseDueAsync(nowUtc, batchSize, $"caller-{i}", TimeSpan.FromMinutes(5), ct);
                foreach (var b in batch)
                {
                    allResults.Add(b.RefId);
                }
            });

        // No duplicate RefIds across concurrent callers (READPAST ensures disjoint sets)
        int distinctCount = allResults.Distinct().Count();
        distinctCount.Should().Be(allResults.Count,
            "UPDLOCK+READPAST should give each caller distinct rows — no RefId duplicates");
    }

    [Fact]
    public async Task LeaseDue_returns_empty_when_no_due_bookmarks()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        // Query far in the past — no bookmarks should be "due" relative to 1970
        DateTime longAgo = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var due = await bookmarkProvider.LeaseDueAsync(
            longAgo, 100, "host-test", TimeSpan.FromMinutes(1), CancellationToken.None);

        due.Should().BeEmpty();
    }
}
