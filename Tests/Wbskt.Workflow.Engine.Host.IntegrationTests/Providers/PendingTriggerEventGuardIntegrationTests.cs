using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// #470: the Queue policy enqueues behind an active run only while that run is still active, and the
/// drain takes the same per-key lock, so an event can never land just after the drain found the queue
/// empty.
/// </summary>
[Collection("SqlEdge")]
public sealed class PendingTriggerEventGuardIntegrationTests(SqlEdgeFixture fixture)
{
    private const string SkipReason = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    [SkippableFact]
    public async Task Enqueueing_behind_a_run_that_has_finished_queues_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        RunRow run = await StartRunAsync();
        await FinishAsync(run);
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        PendingTriggerEventRow? queued = await provider.EnqueueBehindActiveRunAsync(run.WorkflowRefId, run.TriggerNodeId, run.CorrelationKey!, "{}", CancellationToken.None);

        queued.Should().BeNull();
        (await provider.DequeueNextAsync(run.WorkflowRefId, run.TriggerNodeId, run.CorrelationKey!, CancellationToken.None)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Enqueueing_behind_an_active_run_queues_the_event()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        RunRow run = await StartRunAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        PendingTriggerEventRow? queued = await provider.EnqueueBehindActiveRunAsync(run.WorkflowRefId, run.TriggerNodeId, run.CorrelationKey!, """{"n":1}""", CancellationToken.None);

        queued.Should().NotBeNull();
        (await provider.DequeueNextAsync(run.WorkflowRefId, run.TriggerNodeId, run.CorrelationKey!, CancellationToken.None))!.Id.Should().Be(queued!.Id);
    }

    [SkippableFact]
    public async Task A_drain_waits_for_an_enqueue_that_saw_the_run_active()
    {
        // The race #470 described: the enqueue has seen the run active, the run finishes and its
        // finalizer drains, all before the enqueue's row is committed. This session plays the enqueue,
        // holding the key's lock with its row inserted but not committed.
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        RunRow run = await StartRunAsync();
        var provider = ProviderFactory.PendingTriggerEvent(fixture.ConnectionString);

        await using var enqueue = new SqlConnection(fixture.ConnectionString);
        await enqueue.OpenAsync();
        await using SqlTransaction tx = (SqlTransaction)await enqueue.BeginTransactionAsync();
        await using (var insert = new SqlCommand(
            """
            DECLARE @Resource NVARCHAR(255) = dbo.TriggerStartLockResource(@WorkflowRefId, @TriggerNodeId, @CorrelationKey);
            EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';
            INSERT INTO dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
            VALUES (@WorkflowRefId, @TriggerNodeId, @CorrelationKey, N'{}');
            """, enqueue, tx))
        {
            insert.Parameters.AddWithValue("@WorkflowRefId", run.WorkflowRefId);
            insert.Parameters.AddWithValue("@TriggerNodeId", run.TriggerNodeId);
            insert.Parameters.AddWithValue("@CorrelationKey", run.CorrelationKey!);
            await insert.ExecuteNonQueryAsync();
        }

        await FinishAsync(run);
        Task<PendingTriggerEventRow?> drain = provider.DequeueNextAsync(run.WorkflowRefId, run.TriggerNodeId, run.CorrelationKey!, CancellationToken.None);
        (await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(1)))).Should().NotBeSameAs(drain);

        await tx.CommitAsync();

        (await drain.WaitAsync(TimeSpan.FromSeconds(10))).Should().NotBeNull();
    }

    private async Task<RunRow> StartRunAsync()
    {
        WorkflowDefinitionRow wd = await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-PTG-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return await ActiveRunSeeder.StartAsync(fixture.ConnectionString, wd.Id, wd.RefId, Guid.NewGuid(), $"device-{Guid.NewGuid():N}");
    }

    private async Task FinishAsync(RunRow run)
    {
        (await ProviderFactory.Run(fixture.ConnectionString).TransitionStatusAsync(run.Id, RunStatus.Running, RunStatus.Succeeded, null, null, CancellationToken.None))
            .Should().BeTrue();
    }
}
