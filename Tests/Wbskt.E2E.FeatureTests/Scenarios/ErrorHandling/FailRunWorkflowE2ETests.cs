using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

/// <summary>
/// Failure-path E2E — proves a FailRun node fails the whole run:
///   • A single FailRun branch escalates the whole run to "Failed".
///   • A fan-out where one branch reaches a FailRun node ends "Failed" even with failFast=false and
///     a healthy sibling: the node fails the run, not just its branch, and the run says Failed rather
///     than Cancelled or PartiallyFailed.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class FailRunWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task FailRunNode_EscalatesRunToFailed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-failrun-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildFailRunDefinition(workflowRefId, clientRefId.ToString());

        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-FailRun-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "failrun-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Failed", "the single FailRun branch fails and there is no completed branch to soften it");

        // The failure must be visible in the history stream with the configured reason.
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
        history.Should().Contain(
            e => e.EventKind == "NodeFailed" && e.PayloadJson != null && e.PayloadJson.Contains("FAIL_RUN"),
            "the FailRun node should emit a NodeFailed history event carrying its error code");
    }

    [SkippableFact]
    public async Task ForkedFailRun_WithHealthySibling_EndsFailed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-partial-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildPartialFailureDefinition(workflowRefId, clientRefId.ToString());

        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Partial-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "partial-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Failed",
            "a FailRun node fails the whole run, whether or not its healthy sibling completed first");

        // The run failed by its own rule: it records a run-level failure and is never cancelled.
        // Whether the healthy sibling sent its command first is a race, so it is not asserted.
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
        history.Should().Contain(e => e.EventKind == "RunFailureRequested",
            "the FailRun node takes the run-level failure path");
        history.Should().NotContain(e => e.EventKind == "RunCancellationRequested",
            "failing the run must not route it through a cancel");

        var commandNodeId = definition.Nodes.OfType<SendClientMessageNode>().Single().NodeId;
        history.Count(e => e.EventKind == "NodeCompleted" && e.NodeId == commandNodeId)
            .Should().BeLessThanOrEqualTo(1, "only the true-routed branch runs the command node");
    }

    private static WorkflowDefinition BuildFailRunDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-FailRun-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddFailRun("intentional E2E failure");

        return builder.BuildAndValidate();
    }

    private static WorkflowDefinition BuildPartialFailureDefinition(Guid workflowRefId, string clientRef)
    {
        var itemsValue = JsonSerializer.SerializeToElement(new[] { true, false });
        var builder = new WorkflowBuilder($"E2E-Partial-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddVariable(VariableScope.Local, VariableOperation.Set, "items", itemsValue)
            .AddParallelForEach("items", pfe =>
            {
                pfe.OnBody(b1 =>
                {
                    // Only one side of the gate runs per item, so Any lets the healthy side through to
                    // the ParallelForEach's Join (which the validator requires) without waiting on the
                    // side that was not taken.
                    b1.AddLogicGate("item", gate =>
                    {
                        gate.OnTrue(b2 => b2.AddClientMessage(clientRef, "OpenVent"));
                        gate.OnFalse(b2 => b2.AddFailRun("intentional sibling failure"));
                    }, JoinMode.Any);
                });
            });

        return builder.BuildAndValidate();
    }
}
