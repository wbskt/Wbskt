using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

[Collection(E2ECollection.Name)]
public sealed class SagaCompensationE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task SequentialSaga_WithCompensation_RollsBackOnFailure()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + policy + client registration ─────────────────────
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var clientName = $"e2e-saga-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, clientName);

        // ── 2. Publish workflow ──────────────────────────────────────────────
        var workflowRefId = Guid.NewGuid();
        var clientRef = clientRefId.ToString();

        // Topology:
        // DeviceTrigger -> Branch A: SendCommand("ChargeCard", Compensation="RefundCard")
        //               -> Branch B: SendCommand("ReserveStock", Compensation="ReleaseStock")
        //               -> Branch C: Delay(1s) -> FailRun
        
        var builder = new WorkflowBuilder($"E2E-SAGA-{workflowRefId:N}", workflowRefId)
            .EnableCompensationOnFailure(true)
            .AddClientTrigger(clientRef, "start", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddClientMessage(clientRef, "ChargeCard", "Charge Card", new CompensationDeclaration { NodeId = Guid.NewGuid(), Kind = "action:clientMessage", Config = JsonSerializer.SerializeToElement(new SendClientMessageConfig { ClientRef = clientRef, Type = "RefundCard", Payload = null }) })
            .AddClientMessage(clientRef, "ReserveStock", "Reserve Stock", new CompensationDeclaration { NodeId = Guid.NewGuid(), Kind = "action:clientMessage", Config = JsonSerializer.SerializeToElement(new SendClientMessageConfig { ClientRef = clientRef, Type = "ReleaseStock", Payload = null }) })
            .AddDelay(TimeSpan.FromSeconds(2))
            .AddFailRun("inventory issue");

        var definition = builder.BuildAndValidate();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // ── 3. Connect the client and count inbound commands ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: clientName,
            PolicyPin: null);

        var messageLock = new object();
        var messageTypes = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (messageType, _) =>
        {
            lock (messageLock)
            {
                messageTypes.Add(messageType);
            }
        };
        await wbsktClient.StartAsync();

        // ── 4. Trigger the workflow ───────────────
        await wbsktClient.SendAsync("start", new { });

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
                lock (messageLock)
                {
                    return Task.FromResult(messageTypes.Count >= 4);
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
            
        if (!arrived)
        {
            var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId);
            var historyJson = JsonSerializer.Serialize(history, JsonOpts);
            arrived.Should().BeTrue($"client must receive original actions and their compensations. Received: {string.Join(", ", messageTypes)}. History: {historyJson}");
        }
        else
        {
            arrived.Should().BeTrue($"client must receive original actions and their compensations. Received: {string.Join(", ", messageTypes)}");
        }

        lock (messageLock)
        {
            messageTypes.Should().Contain(new[] { "ChargeCard", "ReserveStock", "RefundCard", "ReleaseStock" });
            
            var chargeIdx = messageTypes.IndexOf("ChargeCard");
            var refundIdx = messageTypes.IndexOf("RefundCard");
            refundIdx.Should().BeGreaterThan(chargeIdx, "refund must happen after charge");
            
            var reserveIdx = messageTypes.IndexOf("ReserveStock");
            var releaseIdx = messageTypes.IndexOf("ReleaseStock");
            releaseIdx.Should().BeGreaterThan(reserveIdx, "release must happen after reserve");
        }
    }
}
