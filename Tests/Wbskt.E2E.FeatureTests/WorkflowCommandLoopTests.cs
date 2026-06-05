using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests;

/// <summary>
/// Task 3 — Full telemetry → workflow → command round-trip.
///
/// Topology:
///   DeviceTrigger (event="telemetry", AllowParallel)
///       └─[default]─► action:command (command="OpenVent")
///
/// The test:
///  1. Registers a user + workspace, creates an AutoApproval policy, registers a device client.
///  2. Builds and publishes a minimal DeviceTrigger → SendCommand workflow definition.
///  3. Connects a WbsktClient for the registered device.
///  4. Sends a "telemetry" event from the device.
///  5. Asserts the device receives an "OpenVent" command within 30 s.
///  6. Polls the run list until the run reaches a terminal status within 30 s.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkflowCommandLoopTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TelemetryEvent_TriggersOpenVentCommand_AndRunCompletes()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + workspace + policy + device registration ─────────
        // Use the seeded admin (all permissions, owns Default Workspace Id=1) so the
        // permission-gated policy/publish calls succeed and the definition's
        // workspaceId:1 matches a real workspace.
        var (userToken, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(userToken, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-vent-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Build the minimal workflow definition ─────────────────────────
        var workflowRefId = Guid.NewGuid();
        var definition = BuildMinimalDefinition(workflowRefId, clientRefId.ToString());

        // Validate locally before publishing — fail fast with a clear message.
        var validator = new WorkflowValidator();
        var validation = validator.Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"workflow definition must be valid before publishing; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var definitionElement = JsonSerializer.SerializeToElement(definition, JsonOpts);

        var publishedRef = await fixture.PublishWorkflowAsync(
            userToken,
            workflowRefId,
            $"E2E-OpenVent-{workflowRefId:N}",
            definitionElement);

        publishedRef.Should().NotBeEmpty("workflow publish must return a valid RefId");

        // ── 3. Connect the WbsktClient with pre-seeded credentials ───────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null   // credentials already in storage; no PIN needed
        );

        var commandTcs = new TaskCompletionSource<(string Action, string? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, payload) =>
        {
            commandTcs.TrySetResult((action, payload?.ToString()));
        };

        await wbsktClient.StartAsync();

        // ── 4. Send a telemetry event from the device ────────────────────────
        await wbsktClient.SendTelemetryAsync("telemetry", new
        {
            sensor = "vent-test",
            value = 42,
            timestamp = DateTime.UtcNow
        });

        // ── 5. Assert the device receives the OpenVent command ───────────────
        var commandReceived = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        if (commandReceived != commandTcs.Task)
        {
            // Gather diagnostics before failing.
            var runs = await fixture.ListRunsAsync(userToken, publishedRef);
            var runDiag = runs.Count == 0
                ? "no runs found"
                : string.Join(", ", runs.Select(r => $"RefId={r.RefId} Status={r.Status}"));

            commandTcs.Task.IsCompleted.Should().BeTrue(
                $"device should have received an OpenVent command within 30 s, but none arrived. " +
                $"Workflow runs: [{runDiag}]. " +
                $"Check that the Workflow Engine is running, triggers are registered, " +
                $"and the Socket host routes commands to the device.");
        }

        var (receivedAction, _) = await commandTcs.Task;
        receivedAction.Should().Be("OpenVent",
            "the workflow action:command node must forward the OpenVent command to the device");

        // ── 6. Poll until the run reaches a terminal state ───────────────────
        var runCompleted = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(userToken, publishedRef);
                return runs.Any(r => IsTerminal(r.Status));
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(2));

        runCompleted.Should().BeTrue(
            "the workflow run must reach a terminal status (Succeeded / Failed) within 30 s");

        var finalRuns = await fixture.ListRunsAsync(userToken, publishedRef);
        finalRuns.Should().Contain(r => IsTerminal(r.Status),
            "at least one run for the workflow must have completed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsTerminal(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";

    /// <summary>
    /// Builds the minimal DeviceTrigger → action:command workflow definition.
    ///
    /// Port conventions:
    ///   - Trigger output port id = "default"  (matches DeviceTriggerExecutor.Continue("default", …))
    ///   - Action input  port id = "in"
    ///   - Action output port id = "out"
    ///
    /// Edge: from=[triggerNodeId,"default"] → to=[actionNodeId,"in"]
    ///
    /// WorkspaceId = 1 corresponds to the seeded Default Workspace owned by the admin
    /// (Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql). PublishedBy = 1
    /// is the seeded root admin user id. The Management host stores both as-is from the
    /// definition JSON.
    /// </summary>
    private static WorkflowDefinition BuildMinimalDefinition(Guid workflowRefId, string deviceRef)
    {
        var triggerNodeId = Guid.NewGuid();
        var actionNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode(
            NodeId: triggerNodeId,
            Name: "Device Trigger",
            Ports:
            [
                new PortDefinition("default", PortDirection.Output, "Out")
            ],
            Config: new DeviceTriggerConfig(
                DeviceRef: deviceRef,
                Event: "telemetry",
                CorrelationKey: null,
                ConcurrencyPolicy: WorkflowConcurrencyPolicy.AllowParallel
            )
        );

        var actionNode = new SendCommandActionNode(
            NodeId: actionNodeId,
            Name: "OpenVent",
            Ports:
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("out", PortDirection.Output, "Out")
            ],
            Config: new SendCommandConfig(
                DeviceRef: deviceRef,
                Command: "OpenVent",
                Payload: null
            )
        );

        var edge = new Edge(
            From: (triggerNodeId, "default"),
            To: (actionNodeId, "in")
        );

        return new WorkflowDefinition(
            WorkflowRefId: workflowRefId,
            Version: 1,
            WorkspaceId: 1,
            Name: $"E2E-OpenVent-{workflowRefId:N}",
            Description: null,
            IsEnabled: true,
            Nodes: [triggerNode, actionNode],
            Edges: [edge],
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1
        );
    }
}
