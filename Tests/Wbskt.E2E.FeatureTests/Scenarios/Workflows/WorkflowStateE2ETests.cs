using System.Net;
using FluentAssertions;
using Wbskt.Api.Client;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Workflows;

/// <summary>
/// Enabling, disabling, deprecating and reinstating a workflow, and what each state lets it do.
///
/// Requires the auth, management and engine hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class WorkflowStateE2ETests(ServicesFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task WF_STATE_01_PublishedWithoutEnable_IsDisabled_AndRefusesARun()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, triggerId) = await PublishManualAsync(token, workspace, enable: false);

        var workflow = await fixture.Management.As(token).GetWorkflowAsync(workspace, workflowRef);
        workflow.State.Should().Be(WorkflowState.Disabled);
        workflow.LiveVersion.Should().BeNull();

        using var run = await fixture.StartManualRunRawAsync(token, workspace, workflowRef, triggerId, null);
        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(run)).Should().Be("WORKFLOW_DISABLED");
    }

    [SkippableFact]
    public async Task WF_STATE_02_EnablingAVersion_MakesItLive_AndRunsStartOnIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, triggerId) = await PublishManualAsync(token, workspace, enable: false);
        var management = fixture.Management.As(token);

        var enabled = await management.EnableWorkflowAsync(workspace, workflowRef, 1);

        enabled.State.Should().Be(WorkflowState.Enabled);
        enabled.LiveVersion.Should().Be(1);
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, triggerId, Timeout);
        (await fixture.GetRunDetailAsync(token, workspace, runRef)).Summary.Version.Should().Be(1);
    }

    [SkippableFact]
    public async Task WF_STATE_03_EnablingAnOlderVersion_MovesNewRunsBackToIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, firstTrigger) = await PublishManualAsync(token, workspace, enable: true);
        await PublishManualAsync(token, workspace, enable: true, workflowRef);
        var management = fixture.Management.As(token);

        var switched = await management.EnableWorkflowAsync(workspace, workflowRef, 1);

        switched.LiveVersion.Should().Be(1);
        (await management.GetWorkflowVersionsAsync(workspace, workflowRef)).Items
            .Select(v => (v.Version, v.IsLive)).Should().Equal((2, false), (1, true));

        // Each version has its own trigger node, so only version 1 being live lets its trigger start a run.
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, firstTrigger, Timeout);
        (await fixture.GetRunDetailAsync(token, workspace, runRef)).Summary.Version.Should().Be(1);
    }

    [SkippableFact]
    public async Task WF_STATE_04_EnablingAVersionThatDoesNotExist_Is404_AndChangesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: true);
        var management = fixture.Management.As(token);

        using var response = await management.EnableWorkflowRawAsync(workspace, workflowRef, 7);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKFLOW_VERSION_NOT_FOUND");
        (await management.GetWorkflowAsync(workspace, workflowRef)).LiveVersion.Should().Be(1);
    }

    [SkippableFact]
    public async Task WF_STATE_05_Disabling_WithCancelRunningRuns_CancelsTheRunsInFlight()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var workflowRef = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-State-{workflowRef:N}", workflowRef)
            .AddManualTrigger(out var triggerId)
            .AddAwaitSignal("never", TimeSpan.FromMinutes(10), out _)
            .BuildAndValidate();
        await PublishAsync(token, workspace, workflowRef, definition, enable: true);
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, triggerId, Timeout);
        await fixture.WaitForBranchParkedAsync(token, workspace, runRef, Timeout);

        var disabled = await fixture.Management.As(token).DisableWorkflowAsync(workspace, workflowRef, cancelRunningRuns: true);

        disabled.State.Should().Be(WorkflowState.Disabled);
        disabled.LiveVersion.Should().BeNull();
        disabled.CancelledRuns.Should().Be(1);
        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Cancelled");
    }

    [SkippableFact]
    public async Task WF_STATE_06_Disabling_WithoutCancel_LetsTheRunsInFlightFinish()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var workflowRef = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-State-{workflowRef:N}", workflowRef)
            .AddManualTrigger(out var triggerId)
            .AddAwaitSignal("go", TimeSpan.FromMinutes(10), out _)
            .BuildAndValidate();
        await PublishAsync(token, workspace, workflowRef, definition, enable: true);
        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, workflowRef, triggerId, Timeout);
        await fixture.WaitForBranchParkedAsync(token, workspace, runRef, Timeout);

        var disabled = await fixture.Management.As(token).DisableWorkflowAsync(workspace, workflowRef);

        disabled.CancelledRuns.Should().Be(0);
        await fixture.SendSignalUntilMatchedAsync(token, workspace, runRef, "go", Timeout);
        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task WF_STATE_07_ADisabledWorkflow_CanStillBePublished_AndPublishingWithEnableTurnsItOn()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: true);
        var management = fixture.Management.As(token);
        await management.DisableWorkflowAsync(workspace, workflowRef);

        await PublishManualAsync(token, workspace, enable: false, workflowRef);
        var stillOff = await management.GetWorkflowAsync(workspace, workflowRef);
        stillOff.Version.Should().Be(2);
        stillOff.State.Should().Be(WorkflowState.Disabled);

        await PublishManualAsync(token, workspace, enable: true, workflowRef);
        var on = await management.GetWorkflowAsync(workspace, workflowRef);
        on.State.Should().Be(WorkflowState.Enabled);
        on.LiveVersion.Should().Be(3);
    }

    [SkippableFact]
    public async Task WF_STATE_08_PublishingWithoutEnable_KeepsTheLiveVersion()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: true);

        await PublishManualAsync(token, workspace, enable: false, workflowRef);

        var workflow = await fixture.Management.As(token).GetWorkflowAsync(workspace, workflowRef);
        workflow.Version.Should().Be(2);
        workflow.State.Should().Be(WorkflowState.Enabled);
        workflow.LiveVersion.Should().Be(1);
    }

    [SkippableFact]
    public async Task WF_STATE_09_ADeprecatedWorkflow_RefusesEnableAndDisable_UntilReinstated()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: true);
        var management = fixture.Management.As(token);

        var deprecated = await management.DeprecateWorkflowAsync(workspace, workflowRef);
        deprecated.State.Should().Be(WorkflowState.Deprecated);
        deprecated.LiveVersion.Should().BeNull();

        using (var enable = await management.EnableWorkflowRawAsync(workspace, workflowRef, 1))
        {
            enable.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ServicesFixture.ReadErrorCodeAsync(enable)).Should().Be("WORKFLOW_DEPRECATED");
        }

        using (var disable = await management.DisableWorkflowRawAsync(workspace, workflowRef))
        {
            disable.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ServicesFixture.ReadErrorCodeAsync(disable)).Should().Be("WORKFLOW_DEPRECATED");
        }

        var reinstated = await management.ReinstateWorkflowAsync(workspace, workflowRef);
        reinstated.State.Should().Be(WorkflowState.Disabled);
        (await management.EnableWorkflowAsync(workspace, workflowRef, 1)).State.Should().Be(WorkflowState.Enabled);
    }

    [SkippableFact]
    public async Task WF_STATE_10_ReinstatingAWorkflowThatIsNotDeprecated_Is409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: true);

        using var response = await fixture.Management.As(token).ReinstateWorkflowRawAsync(workspace, workflowRef);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKFLOW_NOT_DEPRECATED");
    }

    [SkippableFact]
    public async Task WF_STATE_11_AnotherWorkspacesWorkflow_IsNotFound_ForEveryStateChange()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(ownerToken, ownerWorkspace, enable: true);
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var stranger = fixture.Management.As(strangerToken);

        HttpResponseMessage[] responses =
        [
            await stranger.EnableWorkflowRawAsync(strangerWorkspace, workflowRef, 1),
            await stranger.DisableWorkflowRawAsync(strangerWorkspace, workflowRef),
            await stranger.DeprecateWorkflowRawAsync(strangerWorkspace, workflowRef),
            await stranger.ReinstateWorkflowRawAsync(strangerWorkspace, workflowRef)
        ];

        foreach (var response in responses)
        {
            using (response)
            {
                response.StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKFLOW_NOT_FOUND");
            }
        }

        (await fixture.Management.As(ownerToken).GetWorkflowAsync(ownerWorkspace, workflowRef)).State.Should().Be(WorkflowState.Enabled);
    }

    [SkippableFact]
    public async Task WF_STATE_12_AWebhookToADisabledWorkflow_StartsNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var workflowRef = Guid.NewGuid();
        var path = $"hook-{Guid.NewGuid():N}";
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-State-{workflowRef:N}", workflowRef)
            .AddWebhookTrigger(path, WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();
        await PublishAsync(token, workspace, workflowRef, definition, enable: true);
        await fixture.FireWebhookUntilRunAsync(token, workspace, workflowRef, path, Timeout);

        await fixture.Management.As(token).DisableWorkflowAsync(workspace, workflowRef);
        string marker = $"after-disable-{Guid.NewGuid():N}";
        await fixture.SendWebhookAsync(workspace, path, new { marker });

        // Nothing reports a delivery that started nothing, so give it the time a live one takes.
        var started = await ServicesFixture.PollAsync(
            async () => (await fixture.ListRunsCarryingAsync(token, workspace, workflowRef, marker)).Count > 0,
            TimeSpan.FromSeconds(8));
        started.Should().BeFalse("a disabled workflow starts no runs");
    }

    [SkippableFact]
    public async Task WF_STATE_13_ASubWorkflowWhoseChildIsDisabled_FailsSayingSo()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (childRef, _) = await PublishManualAsync(token, workspace, enable: false);

        var parentRef = Guid.NewGuid();
        WorkflowDefinition parent = new WorkflowBuilder($"E2E-Parent-{parentRef:N}", parentRef)
            .AddManualTrigger(out var parentTrigger)
            .AddSubWorkflow(childRef)
            .BuildAndValidate();
        await PublishAsync(token, workspace, parentRef, parent, enable: true);

        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, parentRef, parentTrigger, Timeout);

        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Failed");
        (await fixture.GetHistoryAsync(token, workspace, runRef))
            .Should().Contain(e => e.PayloadJson != null && e.PayloadJson.Contains("SUBWORKFLOW_CHILD_DISABLED"));
    }

    [SkippableFact]
    public async Task WF_STATE_14_ATestRun_IsAllowedWhileDisabled()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (workflowRef, _) = await PublishManualAsync(token, workspace, enable: false);

        using var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/workflows/{workflowRef}/test-runs"),
            token,
            new StartTestRunRequest(TestRunSource.Live));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await WbsktApiClient.ReadAsync<StartTestRunResponse>(response);
        var run = await fixture.WaitForRunTerminalAsync(token, workspace, started.RunRefId, Timeout);
        run!.IsTest.Should().BeTrue();
        run.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task WF_STATE_15_APublishThatLeavesOutEnable_Is400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var workflowRef = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-State-{workflowRef:N}", workflowRef)
            .AddManualTrigger(out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        // Bespoke: the typed request cannot leave a required field out.
        using var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/workflows"),
            token,
            new { refId = workflowRef, name = $"E2E-State-{workflowRef:N}", definition });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<(Guid WorkflowRef, Guid TriggerId)> PublishManualAsync(string token, Guid workspace, bool enable, Guid? workflowRef = null)
    {
        var refId = workflowRef ?? Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-State-{refId:N}", refId)
            .AddManualTrigger(out var triggerId)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();
        await PublishAsync(token, workspace, refId, definition, enable);
        return (refId, triggerId);
    }

    private async Task PublishAsync(string token, Guid workspace, Guid workflowRef, WorkflowDefinition definition, bool enable)
    {
        using var response = await fixture.Management.As(token).PublishWorkflowRawAsync(
            workspace, WorkflowRequests.Publish(workflowRef, $"E2E-State-{workflowRef:N}", definition, enable));
        await WbsktApiClient.EnsureSuccessAsync(response);
    }
}
