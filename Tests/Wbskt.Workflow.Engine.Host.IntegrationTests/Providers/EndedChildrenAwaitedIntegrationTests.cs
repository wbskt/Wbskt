using FluentAssertions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Run_GetEndedChildrenAwaited: the child runs a parked sub-workflow step is still waiting on after they
/// ended, so the orphan GC can raise their completion again (#769).
/// </summary>
[Collection("SqlEdge")]
public sealed class EndedChildrenAwaitedIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task An_ended_child_a_parent_still_waits_on_is_listed_and_a_running_one_is_not()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);
        RunRow parent = await CreateRunAsync();
        RunRow ended = await CreateRunAsync();
        RunRow running = await CreateRunAsync();
        RunRow endedUnawaited = await CreateRunAsync();
        DateTime completedAt = DateTime.UtcNow.AddMinutes(-10);
        await runProvider.SetTerminalAsync(ended.Id, RunStatus.Succeeded, completedAt, CancellationToken.None);
        await runProvider.SetTerminalAsync(endedUnawaited.Id, RunStatus.Succeeded, completedAt, CancellationToken.None);
        await ParkOnAsync(bookmarkProvider, parent.Id, ended.RefId);
        await ParkOnAsync(bookmarkProvider, parent.Id, running.RefId);

        IReadOnlyCollection<RunRow> listed = await runProvider.GetEndedChildrenAwaitedAsync(DateTime.UtcNow, 1000, CancellationToken.None);
        IReadOnlyCollection<RunRow> beforeTheyEnded = await runProvider.GetEndedChildrenAwaitedAsync(completedAt.AddMinutes(-1), 1000, CancellationToken.None);

        listed.Should().Contain(run => run.Id == ended.Id && run.Status == RunStatus.Succeeded);
        listed.Should().NotContain(run => run.Id == running.Id || run.Id == endedUnawaited.Id);
        beforeTheyEnded.Should().NotContain(run => run.Id == ended.Id);
    }

    private async Task<RunRow> CreateRunAsync()
    {
        WorkflowDefinitionRow wd = await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 1, WorkspaceId = 1,
            Name = $"IT-WD-CHILD-{Guid.NewGuid():N}", Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""", PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return await ProviderFactory.Run(fixture.ConnectionString).CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id, WorkflowRefId = wd.RefId, WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = RunStatus.Running,
            StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
    }

    private static Task<BookmarkRow> ParkOnAsync(Wbskt.Workflow.Providers.BookmarkProvider bookmarkProvider, int parentRunId, Guid childRunRefId) =>
        bookmarkProvider.CreateAsync(new BookmarkRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = parentRunId, BranchRefId = Guid.NewGuid(), NodeId = Guid.NewGuid(),
            WakeConditionKind = "childRunCompleted", MatchKey = $"child-completed:{childRunRefId}",
            WakeConditionJson = $$"""{"childRunId":"{{childRunRefId}}"}""", ExpiresAt = null, TtlPort = null,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
}
