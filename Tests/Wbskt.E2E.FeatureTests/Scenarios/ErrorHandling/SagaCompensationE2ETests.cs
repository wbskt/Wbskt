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

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

[Collection(E2ECollection.Name)]
public sealed class SagaCompensationE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task SequentialSaga_WithCompensation_RollsBackOnFailure()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + policy + device registration ─────────────────────
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-saga-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Publish workflow ──────────────────────────────────────────────
        var workflowRefId = Guid.NewGuid();
        var deviceRef = clientRefId.ToString();

        // Topology:
        // DeviceTrigger -> Branch A: SendCommand("ChargeCard", Compensation="RefundCard")
        //               -> Branch B: SendCommand("ReserveStock", Compensation="ReleaseStock")
        //               -> Branch C: Delay(1s) -> FailRun
        
        var builder = new WorkflowBuilder($"E2E-SAGA-{workflowRefId:N}", workflowRefId)
            .EnableCompensationOnFailure(true)
            .AddDeviceTrigger(deviceRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddSendCommand(deviceRef, "ChargeCard", "Charge Card", new CompensationDeclaration(
                Guid.NewGuid(), "action:command", JsonSerializer.SerializeToElement(new SendCommandConfig(deviceRef, "RefundCard", null))))
            .AddSendCommand(deviceRef, "ReserveStock", "Reserve Stock", new CompensationDeclaration(
                Guid.NewGuid(), "action:command", JsonSerializer.SerializeToElement(new SendCommandConfig(deviceRef, "ReleaseStock", null))))
            .AddDelay(TimeSpan.FromSeconds(2))
            .AddFailRun("inventory issue");

        var definition = builder.BuildAndValidate();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // ── 3. Connect the device and count inbound commands ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

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

        // ── 4. Trigger the workflow ───────────────
        await wbsktClient.SendTelemetryAsync("start", new { });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // ── 5. The run should eventually finalize as Failed (after compensations) ─
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Failed", "the FailRun node terminates the run");

        // ── 6. Verify compensation commands arrived ────
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Count >= 4);
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
            
        if (!arrived)
        {
            var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
            var historyJson = JsonSerializer.Serialize(history, JsonOpts);
            arrived.Should().BeTrue($"device must receive original actions and their compensations. Received: {string.Join(", ", commands)}. History: {historyJson}");
        }
        else
        {
            arrived.Should().BeTrue($"device must receive original actions and their compensations. Received: {string.Join(", ", commands)}");
        }

        lock (commandLock)
        {
            commands.Should().Contain(new[] { "ChargeCard", "ReserveStock", "RefundCard", "ReleaseStock" });
            
            var chargeIdx = commands.IndexOf("ChargeCard");
            var refundIdx = commands.IndexOf("RefundCard");
            refundIdx.Should().BeGreaterThan(chargeIdx, "refund must happen after charge");
            
            var reserveIdx = commands.IndexOf("ReserveStock");
            var releaseIdx = commands.IndexOf("ReleaseStock");
            releaseIdx.Should().BeGreaterThan(reserveIdx, "release must happen after reserve");
        }
    }
}
