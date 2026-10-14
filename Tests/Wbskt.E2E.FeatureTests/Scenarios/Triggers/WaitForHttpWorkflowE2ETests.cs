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
/// WaitForHttp round-trip — proves the third bookmark wake path (the public http-wake callback).
///
/// Topology:
///   DeviceTrigger (event="telemetry") ─► WaitForHttp(ttl=15m) ─► action:clientMessage (OpenVent)
///
/// Telemetry starts the run, which parks on an http-wake bookmark keyed to the author-defined wake
/// token. The command must NOT fire yet. An external caller POSTs the public wake callback with that
/// token, which the engine maps to the same key, resumes the branch, and sends the command.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class WaitForHttpWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TelemetryParksOnWaitForHttp_Callback_ResumesAndSendsCommand()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-http-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        // The author pins the wake token at design time; the external caller (this test) uses the
        // same value to hit the public callback. High-entropy so it stands in for a real secret.
        var wakeToken = Guid.NewGuid().ToString("N");
        var definition = BuildHttpWaitDefinition(workflowRefId, clientRefId.ToString(), wakeToken);
        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Http-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        var commandTcs = new TaskCompletionSource<(string Action, string? Payload)>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, payload, _) => commandTcs.TrySetResult((action, payload?.ToString()));
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "http-test", value = 1 });

        // Wait until the run exists, parked at WaitForHttp on the author-defined wake token.
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));

        // The gate must block before the callback. The branch parking at WaitForHttp, ahead of the
        // command node, is what shows it held; the command cannot be sent until something wakes it.
        await fixture.WaitForBranchParkedAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        commandTcs.Task.IsCompleted.Should().BeFalse("the command must not fire before the http-wake callback");

        // External callback resumes the parked branch, using the design-time wake token. The callback
        // is opaque (202), so the resume is observed via the command firing, not the response. The
        // bookmark is written just after the park is recorded, so the callback is repeated until the
        // command comes: a wake that finds no bookmark is dropped, and one after the resume finds
        // the bookmark already gone.
        await ServicesFixture.PollUntilAsync(
            "the http-wake callback to resume the branch and send the command",
            async () =>
            {
                await fixture.SendHttpWakeAsync(wakeToken, new { approved = true });
                var arrived = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(3))) == commandTcs.Task;
                return (arrived, arrived ? "command received" : "no command yet");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.Zero);
        (await commandTcs.Task).Action.Should().Be("OpenVent");

        // Server side, the command node ran only once the wake resumed the branch.
        var commandNodeId = definition.Nodes.OfType<SendClientMessageNode>().Single().NodeId;
        var commandCompleted = await fixture.WaitForHistoryEventAsync(
            token, workspaceRef, runRefId,
            e => e.EventKind == "NodeCompleted" && e.NodeId == commandNodeId,
            "the command node's NodeCompleted",
            TimeSpan.FromSeconds(30));
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
        var resumedByWake = history.FirstOrDefault(e => e.EventKind == "BranchResumed");
        resumedByWake.Should().NotBeNull("the wake resumes the parked branch");
        commandCompleted.Timestamp.Should().BeOnOrAfter(resumedByWake!.Timestamp, "the command is sent only after the wake");
    }

    private static WorkflowDefinition BuildHttpWaitDefinition(Guid workflowRefId, string clientRef, string wakeToken)
    {
        var builder = new WorkflowBuilder($"E2E-Http-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddWaitForHttp(TimeSpan.FromMinutes(15), timeout =>
            {
                timeout.OnSuccess(b => b.AddClientMessage(clientRef, "OpenVent"));
            }, token: wakeToken);

        return builder.BuildAndValidate();
    }
}
