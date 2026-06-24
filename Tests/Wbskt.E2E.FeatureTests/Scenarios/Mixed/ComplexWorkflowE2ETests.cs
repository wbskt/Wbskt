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
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
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
/// command. We assert: the command is gated by the delay (arrives no earlier than ~the duration),
/// the run reaches Succeeded, and the history shows the durable park plus the node sequence.
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

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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
        wbsktClient.OnMessageReceived += (action, _) => commandTcs.TrySetResult(action);
        await wbsktClient.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "complex-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        // The command must be gated by the durable delay — it must not arrive almost immediately.
        var early = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        early.Should().NotBe(commandTcs.Task, "the Delay node must hold the branch — the command must not fire before the timer");

        // The command must arrive once the timer fires (with headroom for scheduler granularity).
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
