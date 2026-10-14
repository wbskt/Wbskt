using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Mixed;

/// <summary>
/// Complex multi-feature flow — chains a conditional gate, a durable timer, an action and an
/// explicit terminal node in a single workflow, proving they compose end-to-end.
///
/// Topology:
///   DeviceTrigger ─► LogicGate("true") ──true──► Delay(5s) ─► action:clientMessage (OpenVent) ─► End
///                                       └─false─► (unused)
///
/// Telemetry starts the run; the gate routes to the true port; the Delay node parks on a durable
/// timer for 5s (BranchParked in history); when the timer fires the branch resumes and sends the
/// command. We assert: the command is gated by the delay (arrives no earlier than ~the duration,
/// and the history shows the branch parked at the Delay node and resumed no sooner than the delay
/// later), the run reaches Succeeded, and the history shows the node sequence.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ComplexWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    [SkippableFact]
    public async Task GateThenDurableDelayThenCommand_DeliversAfterDelay_AndRunSucceeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-complex-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildComplexDefinition(workflowRefId, clientRefId.ToString());

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Complex-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);

        var commandTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _, _) => commandTcs.TrySetResult(action);
        await wbsktClient.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        await wbsktClient.SendAsync("telemetry", new { sensor = "complex-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        // The command must arrive once the timer fires (with headroom for scheduler granularity).
        // That it did not arrive before is shown by the stopwatch below and, independent of how fast
        // this runner is, by the park and resume the run's history records.
        var resumed = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        resumed.Should().Be(commandTcs.Task, "the resumed branch must send the command after the delay");
        (await commandTcs.Task).Should().Be("OpenVent");
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(
            Delay - TimeSpan.FromSeconds(1.5),
            "the command should be gated until roughly the configured delay has elapsed");

        // The run finalizes as Succeeded.
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded");

        // History must show the durable park and the node lifecycle.
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
        history.Should().Contain(e => e.EventKind == "BranchParked",
            "the Delay node must durably park the branch (recorded as BranchParked)");

        // The Delay node held the branch: it parked there and was resumed by the timer no sooner than
        // the delay later, and the command node only completed after that.
        var delayNodeId = definition.Nodes.OfType<DelayNode>().Single().NodeId;
        var commandNodeId = definition.Nodes.OfType<SendClientMessageNode>().Single().NodeId;
        var parkedAtDelay = history.First(e => e.EventKind == "BranchParked" && e.NodeId == delayNodeId);
        var resumedByTimer = history.FirstOrDefault(e =>
            e.EventKind == "BranchResumed" && e.BranchRefId == parkedAtDelay.BranchRefId && e.Timestamp >= parkedAtDelay.Timestamp);
        resumedByTimer.Should().NotBeNull("the timer must resume the branch parked at the Delay node");
        (resumedByTimer!.Timestamp - parkedAtDelay.Timestamp).Should().BeGreaterThanOrEqualTo(
            Delay - TimeSpan.FromSeconds(1),
            "the branch must stay parked for the configured delay");
        history.Should().ContainSingle(e => e.EventKind == "NodeCompleted" && e.NodeId == commandNodeId)
            .Which.Timestamp.Should().BeOnOrAfter(resumedByTimer.Timestamp, "the command is sent only once the timer has resumed the branch");
        history.Count(e => e.EventKind == "NodeCompleted").Should().BeGreaterThanOrEqualTo(3,
            "the gate, delay and command nodes should each complete");
    }

    private static WorkflowDefinition BuildComplexDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-Complex-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddLogicGate("true", gate =>
            {
                gate.OnTrue(b => b.AddDelay(Delay).AddClientMessage(clientRef, "OpenVent").AddEnd());
            });

        return builder.BuildAndValidate();
    }
}
