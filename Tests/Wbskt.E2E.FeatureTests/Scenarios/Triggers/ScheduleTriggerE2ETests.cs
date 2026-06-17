using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

[Collection(E2ECollection.Name)]
public sealed class ScheduleTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task ScheduleTrigger_StartsRunBasedOnCron()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-sched-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        
        // * * * * * = every minute. It will fire at the top of the next minute.
        var definition = new WorkflowBuilder($"E2E-Schedule-{workflowRefId:N}", workflowRefId)
            .AddScheduleTrigger("* * * * *", out _)
            .AddSendCommand(clientRefId.ToString(), "ScheduleFired")
            .Build();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Schedule-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // The schedule trigger will start the run at the top of the minute.
        // Waiting for the terminal run directly is sufficient to prove the trigger worked.
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(90));
        runRefId.Should().NotBe(Guid.Empty, "schedule trigger should have started the run at the top of the minute");
        
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    private static WorkflowDefinition BuildScheduleDefinition(Guid workflowRefId, string clientRefId)
    {
        var builder = new WorkflowBuilder($"E2E-Schedule-{workflowRefId:N}", workflowRefId)
            .AddScheduleTrigger("* * * * *", out Guid triggerNodeId);

        // A SendCommand won't work easily here since Schedule Trigger events don't natively include the client context payload.
        builder.SetHead(triggerNodeId, "default");

        return builder.Build();
    }
}
