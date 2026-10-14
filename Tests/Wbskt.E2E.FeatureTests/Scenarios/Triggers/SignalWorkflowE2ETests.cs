using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// Signal wait round-trip — proves the bookmark "inbound" wake path and the match-key
/// unification end-to-end (a human-in-the-loop approval gate).
///
/// Topology:
///   DeviceTrigger (event="telemetry") ─► AwaitSignal("approve") ─► action:clientMessage (OpenVent)
///
/// Telemetry starts the run, which reaches AwaitSignal and parks on a bookmark whose match
/// key is "signal:approve:{runRefId}". The command must NOT fire yet — the branch is blocked.
/// An operator then POSTs the "approve" signal to that run; CorrelationKeyResolver maps it to
/// the same "signal:approve:{runRefId}" key, BookmarkResumer matches the bookmark, the branch
/// resumes and sends the command. We assert: no command before the signal, command after it.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class SignalWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TelemetryParksOnAwaitSignal_OperatorSignal_ResumesAndSendsCommand()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Own workspace + policy + client registration ─────────────────
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-signal-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Publish DeviceTrigger → AwaitSignal("approve") → command ──────
        var workflowRefId = Guid.NewGuid();
        var definition = BuildSignalDefinition(workflowRefId, clientRefId.ToString());

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Signal-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // ── 3. Connect the client and arm a command listener ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

        var commandTcs = new TaskCompletionSource<(string Action, string? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, payload, _) => commandTcs.TrySetResult((action, payload?.ToString()));
        await wbsktClient.StartAsync();

        // ── 4. Send telemetry → run starts and parks at AwaitSignal ──────────
        await wbsktClient.SendAsync("telemetry", new { sensor = "signal-test", value = 1 });

        // Wait until the run exists (it is now parked, status still "Running").
        Guid runRefId = Guid.Empty;
        var runAppeared = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
                if (runs.Count > 0)
                {
                    runRefId = runs[0].RefId;
                    return true;
                }
                return false;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(1));
        runAppeared.Should().BeTrue("telemetry should have started a run that parks on the signal");

        // ── 5. The gate must block: no command before the approval signal ────
        // The branch parking on AwaitSignal, ahead of the command node, is what shows the gate held;
        // the command cannot be sent until something resumes it.
        await fixture.WaitForBranchParkedAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        commandTcs.Task.IsCompleted.Should().BeFalse(
            "the command must NOT be sent before the approval signal — the branch must stay parked on AwaitSignal");

        // ── 6. Operator approves → branch resumes via the inbound bookmark ──
        // The bookmark is written just after the park is recorded, so the signal is repeated until it
        // matches; the run has nothing else that could resume it.
        await fixture.SendSignalUntilMatchedAsync(token, workspaceRef, runRefId, "approve", TimeSpan.FromSeconds(15));

        // ── 7. Now the command must arrive ───────────────────────────────────
        var resumed = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        resumed.Should().Be(commandTcs.Task, "the approved branch must resume and send the command");
        var (receivedAction, _) = await commandTcs.Task;
        receivedAction.Should().Be("OpenVent");

        // ── 8. The run finalizes ─────────────────────────────────────────────
        var completed = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
                return runs.Any(r => IsTerminal(r.Status));
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(2));
        completed.Should().BeTrue("the run must finalize after the signalled branch completes");

        // ── 9. Server side, the command node ran only once the signal resumed the branch ──
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
        var commandNodeId = definition.Nodes.OfType<SendClientMessageNode>().Single().NodeId;
        var resumedBySignal = history.FirstOrDefault(e => e.EventKind == "BranchResumed");
        resumedBySignal.Should().NotBeNull("the signal resumes the parked branch");
        history.Should().ContainSingle(e => e.EventKind == "NodeCompleted" && e.NodeId == commandNodeId)
            .Which.Timestamp.Should().BeOnOrAfter(resumedBySignal!.Timestamp, "the command is sent only after the signal");
    }

    private static bool IsTerminal(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";

    private static WorkflowDefinition BuildSignalDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-Signal-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddAwaitSignal("approve", null, timeout => 
            {
                timeout.OnSuccess(b => b.AddClientMessage(clientRef, "OpenVent"));
            });

        return builder.BuildAndValidate();
    }
}
