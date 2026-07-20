using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Phase 1.2 — Run_GetStuck must only return runs with no live work (no bookmark, no
/// active/waiting/compensating branch), not merely "old and non-terminal".
/// </summary>
[Collection("SqlEdge")]
public sealed class RunGetStuckIntegrationTests(SqlEdgeFixture fixture, ITestOutputHelper output)
{
    private async Task<(WorkflowDefinitionRow Wd, RunRow Run)> CreateRunAsync(DateTime createdAt)
    {
        var wdProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        RunRow run = await runProvider.CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id,
            WorkflowRefId = wd.RefId,
            WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = null,
            Status = "Running",
            StartedAt = createdAt,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0m,
            CreatedAt = createdAt
        }, CancellationToken.None);

        return (wd, run);
    }

    [Fact]
    public async Task GetStuckRuns_excludes_run_with_a_live_bookmark()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        DateTime old = DateTime.UtcNow.AddHours(-1);
        (_, RunRow run) = await CreateRunAsync(old);

        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        await bookmarkProvider.CreateAsync(new BookmarkRow
        {
            Id = 0,
            RefId = Guid.NewGuid(),
            RunId = run.Id,
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            WakeConditionKind = "timer",
            MatchKey = string.Empty,
            WakeConditionJson = "{}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(45),
            TtlPort = null,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        IReadOnlyCollection<RunRow> stuck = await runProvider.GetStuckRunsAsync(DateTime.UtcNow, 100, CancellationToken.None);

        stuck.Should().NotContain(r => r.Id == run.Id);
    }

    [Fact]
    public async Task GetStuckRuns_excludes_run_with_an_active_branch()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        DateTime old = DateTime.UtcNow.AddHours(-1);
        (_, RunRow run) = await CreateRunAsync(old);

        var branchProvider = ProviderFactory.Branch(fixture.ConnectionString);
        await branchProvider.CreateAsync(new BranchRow
        {
            Id = 0,
            RefId = Guid.NewGuid(),
            RunId = run.Id,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.NewGuid(),
            Status = "Active",
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CompensationStackJson = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RowVersion = Array.Empty<byte>()
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        IReadOnlyCollection<RunRow> stuck = await runProvider.GetStuckRunsAsync(DateTime.UtcNow, 100, CancellationToken.None);

        stuck.Should().NotContain(r => r.Id == run.Id);
    }

    [Fact]
    public async Task GetStuckRuns_returns_run_with_no_bookmarks_and_no_live_branches()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        DateTime old = DateTime.UtcNow.AddHours(-1);
        (_, RunRow run) = await CreateRunAsync(old);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        IReadOnlyCollection<RunRow> stuck = await runProvider.GetStuckRunsAsync(DateTime.UtcNow, 100, CancellationToken.None);

        stuck.Should().Contain(r => r.Id == run.Id);
    }

    [Fact]
    public async Task GetStuckRuns_excludes_run_not_yet_past_cutoff()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        DateTime recent = DateTime.UtcNow;
        (_, RunRow run) = await CreateRunAsync(recent);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        IReadOnlyCollection<RunRow> stuck = await runProvider.GetStuckRunsAsync(DateTime.UtcNow.AddMinutes(-30), 100, CancellationToken.None);

        stuck.Should().NotContain(r => r.Id == run.Id);
    }
}
