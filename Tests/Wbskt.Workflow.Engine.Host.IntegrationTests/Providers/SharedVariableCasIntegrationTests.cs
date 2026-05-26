using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.6 — SharedVariable CompareAndSet: verifies that version-mismatch (stale expected
/// value) returns 0 rows affected and does not overwrite the stored value.
/// </summary>
[Collection("SqlEdge")]
public sealed class SharedVariableCasIntegrationTests(SqlEdgeFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Initialize_then_Get_round_trips()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        SharedVariableRow created = await provider.InitializeAsync(
            workflowRefId, "counter", "Number", "0", CancellationToken.None);

        created.WorkflowRefId.Should().Be(workflowRefId);
        created.VarName.Should().Be("counter");
        created.ValueJson.Should().Be("0");

        SharedVariableRow retrieved = await provider.GetByWorkflowRefIdNameAsync(
            workflowRefId, "counter", CancellationToken.None);

        retrieved.Id.Should().Be(created.Id);
        retrieved.ValueJson.Should().Be("0");
    }

    [Fact]
    public async Task SetAsync_overwrites_value()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        await provider.InitializeAsync(workflowRefId, "val", "String", "\"hello\"", CancellationToken.None);

        await provider.SetAsync(workflowRefId, "val", "\"world\"", CancellationToken.None);

        SharedVariableRow result = await provider.GetByWorkflowRefIdNameAsync(
            workflowRefId, "val", CancellationToken.None);

        result.ValueJson.Should().Be("\"world\"");
    }

    [Fact]
    public async Task CompareAndSet_with_correct_expected_value_succeeds()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        await provider.InitializeAsync(workflowRefId, "cas-var", "String", "\"initial\"", CancellationToken.None);

        int rowsAffected = await provider.CompareAndSetAsync(
            workflowRefId, "cas-var", "\"initial\"", "\"updated\"", CancellationToken.None);

        rowsAffected.Should().Be(1);

        SharedVariableRow result = await provider.GetByWorkflowRefIdNameAsync(
            workflowRefId, "cas-var", CancellationToken.None);

        result.ValueJson.Should().Be("\"updated\"");
    }

    [Fact]
    public async Task CompareAndSet_with_stale_expected_value_returns_zero_rows_affected()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        await provider.InitializeAsync(workflowRefId, "cas-var2", "String", "\"v1\"", CancellationToken.None);

        int rowsAffected = await provider.CompareAndSetAsync(
            workflowRefId, "cas-var2", "\"stale\"", "\"v2\"", CancellationToken.None);

        rowsAffected.Should().Be(0, "CAS must fail when expected value does not match");

        SharedVariableRow result = await provider.GetByWorkflowRefIdNameAsync(
            workflowRefId, "cas-var2", CancellationToken.None);

        result.ValueJson.Should().Be("\"v1\"", "value should be unchanged after failed CAS");
    }

    [Fact]
    public async Task Concurrent_writers_exactly_one_wins_the_cas()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        await provider.InitializeAsync(workflowRefId, "concurrent-cas", "String", "\"original\"", CancellationToken.None);

        int[] results = new int[10];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 10),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (i, ct) =>
            {
                var p = ProviderFactory.SharedVariable(fixture.ConnectionString);
                results[i] = await p.CompareAndSetAsync(
                    workflowRefId, "concurrent-cas", "\"original\"", $"\"winner-{i}\"", ct);
            });

        results.Count(r => r == 1).Should().Be(1, "exactly one concurrent CAS should win");
        results.Count(r => r == 0).Should().Be(9, "all other concurrent CAS calls should lose");
    }
}
