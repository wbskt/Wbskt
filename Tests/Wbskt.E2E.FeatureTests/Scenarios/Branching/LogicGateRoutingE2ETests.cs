using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
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

        var deviceName = $"e2e-gate-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildLogicGateDefinition(workflowRefId, clientRefId.ToString());

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Gate-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);

        var commandLock = new object();
        var commandsReceived = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commandsReceived.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        // True Event
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "gate-test", value = true });
        
        // False Event
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "gate-test", value = false });

        // Wait for both commands to arrive
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commandsReceived.Count == 2);
                }
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("exactly two commands must be sent for the two telemetry events");

        lock (commandLock)
        {
            commandsReceived.Should().Contain(new[] { "AlertHigh", "AlertLow" }, "the engine must correctly evaluate expressions and route to the expected branches");
        }
    }

    private static WorkflowDefinition BuildLogicGateDefinition(Guid workflowRefId, string deviceRef)
    {
        // Topology:
        // DeviceTrigger -> LogicGate ("trigger.payload.data.value")
        //                 --true--> SendCommand("AlertHigh")
        //                 --false-> SendCommand("AlertLow")

        var builder = new WorkflowBuilder($"E2E-Gate-{workflowRefId:N}", workflowRefId)
            .AddDeviceTrigger(deviceRef)
            .AddLogicGate("trigger.payload.payload.data.value", logic => 
            {
                logic.OnTrue(b => b.AddSendCommand(deviceRef, "AlertHigh"));
                logic.OnFalse(b => b.AddSendCommand(deviceRef, "AlertLow"));
            });

        return builder.BuildAndValidate();
    }
}
