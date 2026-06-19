using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

/// <summary>
/// Failure-path E2E — proves the two terminal failure aggregations:
///   • A single FailRun branch escalates the whole run to "Failed".
///   • A fan-out where one branch fails and another completes (failFast=false) aggregates to
///     "PartiallyFailed" — the failing branch does NOT cancel its healthy sibling.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class FailRunWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task FailRunNode_EscalatesRunToFailed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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

        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "failrun-test", value = 1 });

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
    public async Task ForkedFailure_WithHealthySibling_AggregatesToPartiallyFailed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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

        var commandLock = new object();
        var commands = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "partial-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("PartiallyFailed",
            "one body branch fails while the healthy sibling completes (failFast is off, so no cancellation)");

        // The healthy sibling must still have delivered its command despite the sibling failure.
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Count >= 1);
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
        arrived.Should().BeTrue("the healthy branch must send its command even though its sibling failed");

        await Task.Delay(TimeSpan.FromSeconds(2));
        lock (commandLock)
        {
            commands.Should().ContainSingle("only the true-routed branch sends a command")
                .Which.Should().Be("OpenVent");
        }
    }

    private static WorkflowDefinition BuildFailRunDefinition(Guid workflowRefId, string deviceRef)
    {
        var triggerNodeId = Guid.NewGuid();
        var failNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode(
            triggerNodeId, "Device Trigger",
            [new PortDefinition("default", PortDirection.Output, "Out")],
            new DeviceTriggerConfig(deviceRef, "telemetry", null, WorkflowConcurrencyPolicy.AllowParallel));

        var failNode = new FailRunNode(
            failNodeId, "Fail The Run",
            [new PortDefinition("in", PortDirection.Input, "In")],
            new FailRunConfig("intentional E2E failure"));

        var edges = new[]
        {
            new Edge((triggerNodeId, "default"), (failNodeId, "in"))
        };

        return new WorkflowDefinition(
            workflowRefId, 1, 1, $"E2E-FailRun-{workflowRefId:N}", null, true,
            [triggerNode, failNode], edges, [], DateTime.UtcNow, 1);
    }

    private static WorkflowDefinition BuildPartialFailureDefinition(Guid workflowRefId, string deviceRef)
    {
        var triggerNodeId = Guid.NewGuid();
        var variableNodeId = Guid.NewGuid();
        var pfeNodeId = Guid.NewGuid();
        var gateNodeId = Guid.NewGuid();
        var actionNodeId = Guid.NewGuid();
        var failNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode(
            triggerNodeId, "Device Trigger",
            [new PortDefinition("default", PortDirection.Output, "Out")],
            new DeviceTriggerConfig(deviceRef, "telemetry", null, WorkflowConcurrencyPolicy.AllowParallel));

        // Two parallel items — one truthy, one falsy — so the gate routes the siblings apart.
        var itemsValue = JsonSerializer.SerializeToElement(new[] { true, false });
        var variableNode = new VariableNode(
            variableNodeId, "Set Items",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("default", PortDirection.Output, "Out")
            ],
            new VariableConfig(VariableScope.Local, VariableOperation.Set, "items", itemsValue));

        var pfeNode = new ParallelForEachNode(
            pfeNodeId, "Parallel For Each",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("body", PortDirection.Output, "Body"),
                new PortDefinition("empty", PortDirection.Output, "Empty")
            ],
            new ParallelForEachConfig("items"));

        // The gate reads the per-branch bound item (a bool) and routes true→command, false→FailRun.
        var gateNode = new LogicGateNode(
            gateNodeId, "Route By Item",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("true", PortDirection.Output, "True"),
                new PortDefinition("false", PortDirection.Output, "False")
            ],
            new LogicGateConfig("item"));

        var actionNode = new SendCommandActionNode(
            actionNodeId, "OpenVent",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("default", PortDirection.Output, "Out")
            ],
            new SendCommandConfig(deviceRef, "OpenVent", null));

        var failNode = new FailRunNode(
            failNodeId, "Fail Branch",
            [new PortDefinition("in", PortDirection.Input, "In")],
            new FailRunConfig("intentional sibling failure"));

        var edges = new[]
        {
            new Edge((triggerNodeId, "default"), (variableNodeId, "in")),
            new Edge((variableNodeId, "default"), (pfeNodeId, "in")),
            new Edge((pfeNodeId, "body"), (gateNodeId, "in")),
            new Edge((gateNodeId, "true"), (actionNodeId, "in")),
            new Edge((gateNodeId, "false"), (failNodeId, "in"))
        };

        return new WorkflowDefinition(
            workflowRefId, 1, 1, $"E2E-Partial-{workflowRefId:N}", null, true,
            [triggerNode, variableNode, pfeNode, gateNode, actionNode, failNode], edges, [], DateTime.UtcNow, 1);
    }
}
