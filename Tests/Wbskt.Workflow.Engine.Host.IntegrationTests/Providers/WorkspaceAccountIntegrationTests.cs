using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.WorkspaceAccounts</c>, the main database's copy of each workspace's plan. Copies arrive from a
/// periodic sync and from events in any order, so a copy must only ever be replaced by a newer one.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkspaceAccountIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_copy_is_written_and_read_back_whole()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var account = new WorkspaceAccount(NewWorkspaceId(), 41, Guid.NewGuid(), PlanCode.Business, new PlanLimitOverrides { Clients = 4_000, CreditsPerMonth = 750_000.5m }, 3);

        int changed = await Provider().UpsertAsync([account], CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.Equal(account, await Provider().FindAsync(account.WorkspaceId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task An_older_revision_never_replaces_a_newer_copy()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        var newer = new WorkspaceAccount(workspaceId, 41, Guid.NewGuid(), PlanCode.Pro, null, 5);
        await Provider().UpsertAsync([newer], CancellationToken.None);

        int changed = await Provider().UpsertAsync([newer with { PlanCode = PlanCode.Free, PlanRevision = 4 }], CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Equal(PlanCode.Pro, (await Provider().FindAsync(workspaceId, CancellationToken.None))!.PlanCode);
    }

    [SkippableFact]
    public async Task A_newer_revision_replaces_the_copy_and_an_identical_one_changes_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        var first = new WorkspaceAccount(workspaceId, 41, Guid.NewGuid(), PlanCode.Free, null, 0);
        var upgraded = first with { PlanCode = PlanCode.Maker, PlanRevision = 1 };
        await Provider().UpsertAsync([first], CancellationToken.None);

        Assert.Equal(1, await Provider().UpsertAsync([upgraded], CancellationToken.None));
        Assert.Equal(0, await Provider().UpsertAsync([upgraded], CancellationToken.None));
        Assert.Equal(upgraded, await Provider().FindAsync(workspaceId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task A_page_mixing_new_changed_and_stale_copies_writes_only_what_is_newer()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        Guid tenantRef = Guid.NewGuid();
        int held = NewWorkspaceId();
        int stale = NewWorkspaceId();
        int fresh = NewWorkspaceId();
        await Provider().UpsertAsync(
        [
            new WorkspaceAccount(held, 41, tenantRef, PlanCode.Free, null, 1),
            new WorkspaceAccount(stale, 41, tenantRef, PlanCode.Pro, null, 9)
        ], CancellationToken.None);

        int changed = await Provider().UpsertAsync(
        [
            new WorkspaceAccount(held, 41, tenantRef, PlanCode.Maker, null, 2),
            new WorkspaceAccount(stale, 41, tenantRef, PlanCode.Free, null, 8),
            new WorkspaceAccount(fresh, 41, tenantRef, PlanCode.Maker, null, 2)
        ], CancellationToken.None);

        Assert.Equal(2, changed);
        Assert.Equal(PlanCode.Maker, (await Provider().FindAsync(held, CancellationToken.None))!.PlanCode);
        Assert.Equal(PlanCode.Pro, (await Provider().FindAsync(stale, CancellationToken.None))!.PlanCode);
        Assert.Equal(PlanCode.Maker, (await Provider().FindAsync(fresh, CancellationToken.None))!.PlanCode);
    }

    [SkippableFact]
    public async Task A_workspace_with_no_copy_is_not_found()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        Assert.Null(await Provider().FindAsync(NewWorkspaceId(), CancellationToken.None));
    }

    [SkippableFact]
    public async Task Retiring_a_workspace_removes_its_copy()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        await Provider().UpsertAsync([new WorkspaceAccount(workspaceId, 41, Guid.NewGuid(), PlanCode.Pro, null, 1)], CancellationToken.None);

        await new WorkspaceRetirementProvider(Configuration()).RetireAsync(workspaceId);

        Assert.Null(await Provider().FindAsync(workspaceId, CancellationToken.None));
    }

    // Workspaces live in the auth database, so any id will do here; a random high one keeps tests apart.
    private static int NewWorkspaceId() => Random.Shared.Next(1_000_000, int.MaxValue);

    private WorkspaceAccountProvider Provider() => new(Configuration());

    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
        .Build();
}
