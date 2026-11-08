using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.WorkflowDraft_*</c> and the draft guards of <c>dbo.WorkflowDefinition_Publish</c>: revisions
/// stop one editor overwriting another, a publish refuses an outdated draft and deletes only the
/// revision it published, and drafts of new workflows have a ceiling of their own.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkflowDraftIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private const int OwnedElsewhereError = 50021;
    private const int DeletedError = 50022;
    private const int VersionMovedError = 50025;
    private const int RevisionMovedError = 50031;
    private const int DraftLimitReachedError = 50032;

    [SkippableFact]
    public async Task Saves_bump_the_revision_and_a_stale_revision_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();

        var first = await drafts.SaveAsync(Save(refId, workspaceId, "one"), expectedRevision: null, maxDrafts: null, CancellationToken.None);
        Assert.Equal(1, first.Revision);
        Assert.Null(first.BaseVersion);
        Assert.Null(first.CurrentVersion);

        var second = await drafts.SaveAsync(Save(refId, workspaceId, "two"), expectedRevision: 1, maxDrafts: null, CancellationToken.None);
        Assert.Equal(2, second.Revision);
        Assert.Equal("two", second.Name);

        // A second editor still on revision 1, and one starting afresh, are both refused.
        await AssertSqlError(RevisionMovedError, () => drafts.SaveAsync(Save(refId, workspaceId, "stale"), 1, null, CancellationToken.None));
        await AssertSqlError(RevisionMovedError, () => drafts.SaveAsync(Save(refId, workspaceId, "fresh"), null, null, CancellationToken.None));

        Assert.Equal("two", (await drafts.FindAsync(refId, workspaceId, CancellationToken.None))!.Name);
    }

    [SkippableFact]
    public async Task A_draft_cannot_claim_another_workspaces_or_a_deleted_workflow()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();

        Guid published = Guid.NewGuid();
        await definitions.InsertAsync(Row(published, workspaceId), CancellationToken.None);
        await AssertSqlError(OwnedElsewhereError, () => drafts.SaveAsync(Save(published, workspaceId + 1, "x"), null, null, CancellationToken.None));

        Guid drafted = Guid.NewGuid();
        await drafts.SaveAsync(Save(drafted, workspaceId, "x"), null, null, CancellationToken.None);
        await AssertSqlError(OwnedElsewhereError, () => drafts.SaveAsync(Save(drafted, workspaceId + 1, "x"), null, null, CancellationToken.None));
        Assert.Null(await drafts.FindAsync(drafted, workspaceId + 1, CancellationToken.None));

        Assert.NotNull(await definitions.DeleteAsync(published, workspaceId, 9, CancellationToken.None));
        await AssertSqlError(DeletedError, () => drafts.SaveAsync(Save(published, workspaceId, "x"), null, null, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Publishing_a_draft_deletes_only_the_revision_published()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();

        await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        var draft = await drafts.SaveAsync(Save(refId, workspaceId, "edit"), null, null, CancellationToken.None);
        Assert.Equal(1, draft.BaseVersion);
        Assert.Equal(1, draft.CurrentVersion);

        // Saved again while the publish of revision 1 was on its way: the newer edit survives it.
        await drafts.SaveAsync(Save(refId, workspaceId, "later"), 1, null, CancellationToken.None);
        var v2 = await definitions.InsertAsync(Row(refId, workspaceId), [], null, new WorkflowPublishGuards(1, DraftRevision: 1), CancellationToken.None);
        Assert.Equal(2, v2.Version);
        var kept = await drafts.FindAsync(refId, workspaceId, CancellationToken.None);
        Assert.Equal(2, kept!.Revision);
        Assert.Equal(2, kept.CurrentVersion);

        await definitions.InsertAsync(Row(refId, workspaceId), [], null, new WorkflowPublishGuards(2, DraftRevision: 2), CancellationToken.None);
        Assert.Null(await drafts.FindAsync(refId, workspaceId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task A_publish_expecting_an_older_version_is_refused_and_keeps_the_draft()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();

        await drafts.SaveAsync(Save(refId, workspaceId, "new"), null, null, CancellationToken.None);
        await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);

        await AssertSqlError(VersionMovedError, () => definitions.InsertAsync(Row(refId, workspaceId), [], null, new WorkflowPublishGuards(0, 1), CancellationToken.None));
        Assert.NotNull(await drafts.FindAsync(refId, workspaceId, CancellationToken.None));
        Assert.Equal(1, (await definitions.FindCurrentByRefIdAsync(refId, CancellationToken.None))!.Version);

        // "Publish anyway" skips the version check.
        var forced = await definitions.InsertAsync(Row(refId, workspaceId), [], null, new WorkflowPublishGuards(null, 1), CancellationToken.None);
        Assert.Equal(2, forced.Version);
        Assert.Null(await drafts.FindAsync(refId, workspaceId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Only_drafts_of_new_workflows_count_against_the_draft_ceiling()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();

        await drafts.SaveAsync(Save(Guid.NewGuid(), workspaceId, "a"), null, maxDrafts: 2, CancellationToken.None);
        Guid second = Guid.NewGuid();
        await drafts.SaveAsync(Save(second, workspaceId, "b"), null, maxDrafts: 2, CancellationToken.None);
        await AssertSqlError(DraftLimitReachedError, () => drafts.SaveAsync(Save(Guid.NewGuid(), workspaceId, "c"), null, 2, CancellationToken.None));

        // A draft of a published workflow is not counted, and editing a counted draft is never refused.
        Guid published = Guid.NewGuid();
        await definitions.InsertAsync(Row(published, workspaceId), CancellationToken.None);
        await drafts.SaveAsync(Save(published, workspaceId, "p"), null, maxDrafts: 2, CancellationToken.None);
        await drafts.SaveAsync(Save(second, workspaceId, "b2"), 1, maxDrafts: 2, CancellationToken.None);

        // Discarding frees a place; a stale discard removes nothing.
        Assert.False(await drafts.DeleteAsync(second, workspaceId, expectedRevision: 1, CancellationToken.None));
        Assert.True(await drafts.DeleteAsync(second, workspaceId, expectedRevision: 2, CancellationToken.None));
        await drafts.SaveAsync(Save(Guid.NewGuid(), workspaceId, "c"), null, maxDrafts: 2, CancellationToken.None);

        Assert.Equal(3, (await drafts.ListAsync(workspaceId, CancellationToken.None)).Count);
    }

    [SkippableFact]
    public async Task The_workflow_list_reports_a_draft_and_deleting_the_workflow_removes_it()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var drafts = ProviderFactory.WorkflowDraft(fixture.ConnectionString);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();
        Guid editor = Guid.NewGuid();

        await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        await definitions.InsertAsync(Row(Guid.NewGuid(), workspaceId), CancellationToken.None);
        await drafts.SaveAsync(Save(refId, workspaceId, "edit") with { UserRefId = editor }, null, null, CancellationToken.None);

        var list = await definitions.GetAllSummariesAsync(workspaceId, 0, 10, CancellationToken.None);
        var withDraft = Assert.Single(list, row => row.Draft is not null);
        Assert.Equal(refId, withDraft.RefId);
        Assert.Equal(new WorkflowDraftSummary(1, 1, editor, withDraft.Draft!.UpdatedAt), withDraft.Draft);

        await definitions.DeleteAsync(refId, workspaceId, 9, CancellationToken.None);
        Assert.Null(await drafts.FindAsync(refId, workspaceId, CancellationToken.None));
    }

    private static int NewWorkspace() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private static async Task AssertSqlError(int number, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(action);
        Assert.Equal(number, ex.Number);
    }

    private static WorkflowDraftSave Save(Guid refId, int workspaceId, string name) =>
        new(refId, workspaceId, name, null, """{"nodes":[],"edges":[]}""", UserId: 7, UserRefId: null);

    private static WorkflowDefinitionRow Row(Guid refId, int workspaceId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = workspaceId,
        Name = "draft-test",
        Description = null,
        IsEnabled = true,
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };
}
