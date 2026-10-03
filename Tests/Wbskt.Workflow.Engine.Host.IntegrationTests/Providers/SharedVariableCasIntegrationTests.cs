using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.6 — SharedVariable CompareAndSet: verifies that version-mismatch (stale expected
/// value) returns 0 rows affected and does not overwrite the stored value.
/// </summary>
[Collection("SqlEdge")]
public sealed class SharedVariableCasIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task Initialize_then_Get_round_trips()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

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

    [SkippableFact]
    public async Task SetAsync_overwrites_value()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        await provider.InitializeAsync(workflowRefId, "val", "String", "\"hello\"", CancellationToken.None);

        await provider.SetAsync(workflowRefId, "val", "\"world\"", CancellationToken.None);

        SharedVariableRow result = await provider.GetByWorkflowRefIdNameAsync(
            workflowRefId, "val", CancellationToken.None);

        result.ValueJson.Should().Be("\"world\"");
    }

    [SkippableFact]
    public async Task CompareAndSet_with_correct_expected_value_succeeds()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

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

    [SkippableFact]
    public async Task CompareAndSet_with_stale_expected_value_returns_zero_rows_affected()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

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

    [SkippableFact]
    public async Task Concurrent_writers_exactly_one_wins_the_cas()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

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

    [SkippableFact]
    public async Task Concurrent_first_increments_all_count()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        Guid workflowRefId = Guid.NewGuid();

        // No row yet: each call either creates the counter or applies its step to the one another
        // call just created. None may be lost to the race on the insert.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 20),
            new ParallelOptions { MaxDegreeOfParallelism = 20 },
            async (_, ct) => await ProviderFactory.SharedVariable(fixture.ConnectionString).IncrementAsync(workflowRefId, "hits", 2, ct));

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        SharedVariableRow result = await provider.GetByWorkflowRefIdNameAsync(workflowRefId, "hits", CancellationToken.None);
        result.VarType.Should().Be("Counter");
        result.ValueJson.Should().Be("40");
    }

    [SkippableFact]
    public async Task Decrement_creates_a_missing_counter_below_zero()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();

        (await provider.DecrementAsync(workflowRefId, "stock", 3, CancellationToken.None)).Should().Be("-3");
        (await provider.IncrementAsync(workflowRefId, "stock", 5, CancellationToken.None)).Should().Be("2");
    }

    [SkippableTheory]
    [InlineData("Json", "5")]
    [InlineData("Counter", "\"lots\"")]
    [InlineData("Counter", "1.5")]
    public async Task Increment_refuses_a_variable_that_is_not_an_integer_counter(string varType, string valueJson)
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.SharedVariable(fixture.ConnectionString);
        Guid workflowRefId = Guid.NewGuid();
        await provider.InitializeAsync(workflowRefId, "v", varType, valueJson, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<SharedVariableNotACounterException>(
            () => provider.IncrementAsync(workflowRefId, "v", 1, CancellationToken.None));

        ex.ErrorCode.Should().Be("VARIABLE_NOT_A_COUNTER");
        (await provider.GetByWorkflowRefIdNameAsync(workflowRefId, "v", CancellationToken.None)).ValueJson.Should().Be(valueJson);
    }
}
