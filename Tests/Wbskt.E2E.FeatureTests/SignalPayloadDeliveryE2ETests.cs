using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests;

/// <summary>
/// Signal payload delivery — proves the inbound-wake path not only resumes the parked branch but
/// also carries the operator-supplied payload into the resumed branch's local state. AwaitSignal
/// promotes the inbound wake body under the friendly "signalPayload" key, which downstream nodes
/// (and observers) can read.
///
/// Topology:
///   DeviceTrigger ─► AwaitSignal("approve") ─► End
///
/// Telemetry parks the branch on the signal bookmark; an operator posts the "approve" signal with
/// a payload; the branch resumes, promotes the payload to "signalPayload", and completes. We assert
/// the run succeeds AND the completed branch's LocalJson contains the delivered payload marker —
/// proving payload delivery end-to-end via the new branch-local-state observability.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class SignalPayloadDeliveryE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task AwaitSignal_PromotesOperatorPayload_IntoResumedBranchState()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-sigpayload-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildSignalDefinition(workflowRefId, clientRefId.ToString());

        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-SigPayload-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "sig-payload-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run that parks on the signal");

        // The branch must still be parked (not terminal) before the signal arrives.
        var detailBefore = await fixture.GetRunDetailAsync(token, workspaceRef, runRefId);
        ServicesFixture.IsTerminalStatus(detailBefore.Summary.Status).Should().BeFalse(
            "the run must remain in flight, parked on AwaitSignal, until the operator signals");

        // Operator approves with a distinctive payload marker.
        const string marker = "operator-7";
        var (matched, outcome) = await fixture.SendSignalAsync(
            token, workspaceRef, runRefId, "approve", new { approvedBy = marker, level = 5 });
        matched.Should().BeTrue($"the 'approve' signal should resume the parked bookmark (outcome was '{outcome}')");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary.Should().NotBeNull("the run must finalize after the signalled branch completes");
        summary!.Status.Should().Be("Succeeded", "the resumed branch runs to End and completes");

        // The resumed branch must carry the operator payload, promoted under "signalPayload".
        var detail = await fixture.GetRunDetailAsync(token, workspaceRef, runRefId);
        detail.Branches.Should().Contain(
            b => b.LocalJson != null
                 && b.LocalJson.Contains("signalPayload")
                 && b.LocalJson.Contains(marker),
            "AwaitSignal must promote the operator-supplied payload into the resumed branch's local state");
    }

    private static WorkflowDefinition BuildSignalDefinition(Guid workflowRefId, string deviceRef)
    {
        var triggerNodeId = Guid.NewGuid();
        var awaitNodeId = Guid.NewGuid();
        var endNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode(
            triggerNodeId, "Device Trigger",
            [new PortDefinition("default", PortDirection.Output, "Out")],
            new DeviceTriggerConfig(deviceRef, "telemetry", null, WorkflowConcurrencyPolicy.AllowParallel));

        var awaitNode = new AwaitSignalNode(
            awaitNodeId, "Await Approval",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("default", PortDirection.Output, "Signalled")
            ],
            new AwaitSignalConfig("approve"));

        var endNode = new EndNode(
            endNodeId, "End",
            [new PortDefinition("in", PortDirection.Input, "In")]);

        var edges = new[]
        {
            new Edge((triggerNodeId, "default"), (awaitNodeId, "in")),
            new Edge((awaitNodeId, "default"), (endNodeId, "in"))
        };

        return new WorkflowDefinition(
            workflowRefId, 1, 1, $"E2E-SigPayload-{workflowRefId:N}", null, true,
            [triggerNode, awaitNode, endNode], edges, [], DateTime.UtcNow, 1);
    }
}
