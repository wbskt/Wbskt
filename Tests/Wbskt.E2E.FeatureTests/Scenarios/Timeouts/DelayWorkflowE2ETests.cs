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

namespace Wbskt.E2E.FeatureTests.Scenarios.Timeouts;

/// <summary>
/// Durable-delay round-trip — proves the bookmark "timer" wake path end-to-end.
///
/// Topology:
///   DeviceTrigger (event="telemetry") ─► Delay(4s) ─► action:command (OpenVent)
///
/// When the telemetry arrives the run starts, reaches the Delay node, parks itself with a
/// timer bookmark, and returns. The BookmarkScheduler (polling ~1s) later leases the due
/// bookmark and re-dispatches the branch, which re-executes the Delay node — now past its
/// wake instant — and continues to the command. We assert the command is received AND that
/// it arrived no sooner than ~the delay, proving the branch genuinely parked and was resumed
/// rather than running straight through.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DelayWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    [SkippableFact]
    public async Task TelemetryEvent_ParksOnDelay_ThenResumesAndSendsCommand()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + policy + device registration ─────────────────────
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-delay-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Publish the DeviceTrigger → Delay → action:command workflow ───
        var workflowRefId = Guid.NewGuid();
        var definition = BuildDelayDefinition(workflowRefId, clientRefId.ToString());

        var validator = new WorkflowValidator();
        var validation = validator.Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token,
            workspaceRef,
            workflowRefId,
            $"E2E-Delay-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // ── 3. Connect the device and arm a command listener ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

        var commandTcs = new TaskCompletionSource<(string Action, string? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, payload) => commandTcs.TrySetResult((action, payload?.ToString()));

        await wbsktClient.StartAsync();

        // ── 4. Send telemetry and time how long until the command comes back ─
        var stopwatch = Stopwatch.StartNew();
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "delay-test", value = 1 });

        var commandReceived = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(40)));
        if (commandReceived != commandTcs.Task)
        {
            var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
            var runDiag = runs.Count == 0
                ? "no runs found"
                : string.Join(", ", runs.Select(r => $"RefId={r.RefId} Status={r.Status}"));
            commandTcs.Task.IsCompleted.Should().BeTrue(
                $"the delayed command should have arrived within 40 s, but none did. Runs: [{runDiag}]. " +
                $"Check the BookmarkScheduler is running and resuming due timer bookmarks.");
        }

        var (receivedAction, _) = await commandTcs.Task;
        stopwatch.Stop();

        // ── 5. Assert it was a genuine durable delay, not a pass-through ─────
        receivedAction.Should().Be("OpenVent");
        stopwatch.Elapsed.Should().BeGreaterThan(Delay.Subtract(TimeSpan.FromSeconds(1)),
            "the command must not arrive before the delay elapses — that would mean the branch did not park on the timer bookmark");

        // ── 6. The run must reach a terminal state ───────────────────────────
        var completed = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
                return runs.Any(r => IsTerminal(r.Status));
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(2));

        completed.Should().BeTrue("the delayed workflow run must finalize after the command is sent");
    }

    private static bool IsTerminal(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";

    private static WorkflowDefinition BuildDelayDefinition(Guid workflowRefId, string deviceRef)
    {
        var builder = new WorkflowBuilder($"E2E-Delay-{workflowRefId:N}", workflowRefId)
            .AddDeviceTrigger(deviceRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(Delay)
            .AddSendCommand(deviceRef, "OpenVent");

        return builder.BuildAndValidate();
    }
}
