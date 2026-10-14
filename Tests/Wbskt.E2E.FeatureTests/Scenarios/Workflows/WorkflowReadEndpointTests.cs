using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Workflows;

/// <summary>
/// The workflow reads no other scenario reaches: one published version by number, the workspace-wide
/// run list, both stats rollups and a workflow's shared variables. Each is hit once through the
/// typed client against a workflow that has run once, and asserted on its shape.
///
/// Requires the auth, management and engine hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class WorkflowReadEndpointTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private const string VariableName = "e2eCounter";

    [SkippableFact]
    public async Task WF_READ_01_AVersionByNumber_IsThatVersion()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (workflowRef, _) = await PublishAsync(token, workspace);

        var version = await fixture.Management.As(token).GetWorkflowVersionAsync(workspace, workflowRef, 1);

        version.RefId.Should().Be(workflowRef);
        version.Version.Should().Be(1);
        version.Status.Should().Be("Published");
        version.Definition.Nodes.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task WF_READ_02_WorkspaceRunsAndBothStats_CountARunThatFinished()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (workflowRef, triggerId) = await PublishAsync(token, workspace);
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, triggerId, Timeout);
        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Succeeded");

        var management = fixture.Management.As(token);

        var runs = await management.GetWorkspaceRunsAsync(workspace);
        runs.Items.Should().Contain(r => r.RefId == runRef);

        var workflowStats = await ServicesFixture.PollUntilAsync<WorkflowStatsResponse>(
            $"the stats of workflow {workflowRef} to count its run",
            async () =>
            {
                var stats = await management.GetWorkflowStatsAsync(workspace, workflowRef);
                return (stats.Counts.Succeeded >= 1, stats, $"counts {stats.Counts}");
            },
            Timeout,
            TimeSpan.FromSeconds(1));
        workflowStats.WorkflowRefId.Should().Be(workflowRef);
        workflowStats.FromUtc.Should().BeBefore(workflowStats.ToUtc);
        workflowStats.Counts.Total.Should().Be(1);
        workflowStats.SuccessRate.Should().Be(1.0);

        var workspaceStats = await ServicesFixture.PollUntilAsync<WorkspaceStatsResponse>(
            $"the stats of workspace {workspace} to count the run",
            async () =>
            {
                var stats = await management.GetWorkspaceStatsAsync(workspace);
                return (stats.Counts.Succeeded >= 1, stats, $"counts {stats.Counts}");
            },
            Timeout,
            TimeSpan.FromSeconds(1));
        workspaceStats.Counts.Total.Should().Be(1, "the workspace is this scenario's own");
        workspaceStats.Workflows.Should().ContainSingle(w => w.WorkflowRefId == workflowRef)
            .Which.Succeeded.Should().Be(1);
    }

    [SkippableFact]
    public async Task WF_READ_03_ASharedVariable_ReadsWhatTheRunWrote_AndCanBeSet()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (workflowRef, triggerId) = await PublishAsync(token, workspace);
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, triggerId, Timeout);
        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Succeeded");

        var management = fixture.Management.As(token);

        // The run created it: a shared variable exists once something has written it.
        var written = await management.GetWorkflowVariableAsync(workspace, workflowRef, VariableName);
        written.WorkflowRefId.Should().Be(workflowRef);
        written.VarName.Should().Be(VariableName);
        written.ValueJson.Should().Be("1");

        var set = await management.SetWorkflowVariableAsync(workspace, workflowRef, VariableName, new SharedVariableSetRequest("42"));
        set.VarName.Should().Be(VariableName);
        set.ValueJson.Should().Be("42");

        (await management.GetWorkflowVariableAsync(workspace, workflowRef, VariableName)).ValueJson.Should().Be("42");
    }

    /// <summary>A manual trigger that writes <see cref="VariableName"/> = 1 as a shared variable.</summary>
    private async Task<(Guid WorkflowRef, Guid TriggerId)> PublishAsync(string token, Guid workspace)
    {
        var workflowRef = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Reads-{workflowRef:N}", workflowRef)
            .AddManualTrigger(out var triggerId)
            .AddVariable(VariableScope.Shared, VariableOperation.Set, VariableName, JsonSerializer.SerializeToElement(1))
            .BuildAndValidate();

        var published = await fixture.PublishWorkflowAsync(
            token, workspace, workflowRef, $"E2E-Reads-{workflowRef:N}", JsonSerializer.SerializeToElement(definition, JsonOpts));

        return (published, triggerId);
    }
}
