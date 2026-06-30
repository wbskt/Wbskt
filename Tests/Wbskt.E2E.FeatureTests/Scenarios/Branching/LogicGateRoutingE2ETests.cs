using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Branching;

[Collection(E2ECollection.Name)]
public sealed class LogicGateRoutingE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task LogicGate_RoutesBranchAccordingToPayloadValue()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var clientName = $"e2e-gate-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, clientName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildLogicGateDefinition(workflowRefId, clientRefId.ToString());

        await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Gate-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName);

        var messageLock = new object();
        var messageTypesReceived = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (type, _) =>
        {
            lock (messageLock)
            {
                messageTypesReceived.Add(type);
            }
        };
        await wbsktClient.StartAsync();

        // True Event
        await wbsktClient.SendAsync("telemetry", new { sensor = "gate-test", value = true });
        
        // False Event
        await wbsktClient.SendAsync("telemetry", new { sensor = "gate-test", value = false });

        // Wait for both commands to arrive
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (messageLock)
                {
                    return Task.FromResult(messageTypesReceived.Count == 2);
                }
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("exactly two commands must be sent for the two telemetry events");

        lock (messageLock)
        {
            messageTypesReceived.Should().Contain(["AlertHigh", "AlertLow"], "the engine must correctly evaluate expressions and route to the expected branches");
        }
    }

    private static WorkflowDefinition BuildLogicGateDefinition(Guid workflowRefId, string clientRef)
    {
        // Topology:
        // DeviceTrigger -> LogicGate ("trigger.payload.data.value")
        //                 --true--> SendCommand("AlertHigh")
        //                 --false-> SendCommand("AlertLow")

        var builder = new WorkflowBuilder($"E2E-Gate-{workflowRefId:N}", workflowRefId)
            .AddTelemetryClientTrigger(clientRef)
            .AddLogicGate("trigger.payload.value", logic => 
            {
                logic.OnTrue(b => b.AddClientMessage(clientRef, "AlertHigh"));
                logic.OnFalse(b => b.AddClientMessage(clientRef, "AlertLow"));
            });

        return builder.BuildAndValidate();
    }
}
