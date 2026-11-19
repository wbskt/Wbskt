using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.E2E.FeatureTests.Scenarios.Mixed;

[Collection(EngineCollection.Name)]
public sealed class SubWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task SubWorkflow_StartsChildAndResumesParentUponChildCompletion()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var clientName = $"e2e-subflow-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, clientName);

        // 1. Build and publish Child workflow
        var childWorkflowRefId = Guid.NewGuid();
        var childDefinition = new WorkflowBuilder($"E2E-Child-{childWorkflowRefId:N}", childWorkflowRefId)
            .AddManualTrigger(out var childTriggerNodeId)
            .AddDelay(TimeSpan.FromSeconds(2))
            .BuildAndValidate();

        await fixture.PublishWorkflowAsync(
            token, workspaceRef, childWorkflowRefId, $"E2E-Child-{childWorkflowRefId:N}",
            JsonSerializer.SerializeToElement(childDefinition, JsonOpts));

        // 2. Build and publish Parent workflow
        var parentWorkflowRefId = Guid.NewGuid();
        var parentDefinition = new WorkflowBuilder($"E2E-Parent-{parentWorkflowRefId:N}", parentWorkflowRefId)
            .AddTelemetryClientTrigger(clientRefId.ToString())
            .AddSubWorkflow(childWorkflowRefId)
            .AddClientMessage(clientRefId.ToString(), "ParentCommand")
            .BuildAndValidate();

        var parentPublishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, parentWorkflowRefId, $"E2E-Parent-{parentWorkflowRefId:N}",
            JsonSerializer.SerializeToElement(parentDefinition, JsonOpts));

        // 3. Connect the client
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName, null);

        var commandLock = new object();
        var commandsReceived = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _, _) =>
        {
            lock (commandLock)
            {
                if (action == "ParentCommand")
                {
                    commandsReceived.Add(action);
                }
            }
        };
        await wbsktClient.StartAsync();

        // 4. Send telemetry to start the parent
        // The parent starts the child through the child's manual trigger, so wait until the engine
        // has picked that trigger up: a manual start of the child is answered with a run once it has.
        await fixture.WaitForManualTriggerLiveAsync(token, workspaceRef, childWorkflowRefId, childTriggerNodeId, TimeSpan.FromSeconds(30));

        await wbsktClient.SendAsync("telemetry", new { sensor = "subworkflow-test" });

        var parentRunRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, parentPublishedRef, TimeSpan.FromSeconds(30));
        parentRunRefId.Should().NotBe(Guid.Empty, "telemetry should have started the parent run");

        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commandsReceived.Count >= 1);
                }
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("parent command must be sent");

        lock (commandLock)
        {
            commandsReceived.Should().ContainInOrder("ParentCommand");
        }

        // 5. Parent completes
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, parentRunRefId, TimeSpan.FromSeconds(30));
        
        var parentDetail = await fixture.GetRunDetailAsync(token, workspaceRef, parentRunRefId);
        var branches = string.Join("\n", parentDetail.Branches.Select(b => $"{b.NodeId} - {b.Status}"));
        
        summary.Should().NotBeNull($"Parent run failed or did not finish: {branches}");
        summary!.Status.Should().Be("Succeeded", $"Parent run failed: {branches}");
    }

    [SkippableFact]
    public async Task SubWorkflow_NamingAnotherTenantsWorkflow_IsRefusedAtPublish()
    {
        // #940: a manual trigger's key is the workflow ref alone, so a step naming another tenant's
        // workflow once started a run there. Publish refuses the ref, reading it as missing.
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (otherToken, otherWorkspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var foreignRef = Guid.NewGuid();
        var foreign = new WorkflowBuilder($"E2E-Foreign-{foreignRef:N}", foreignRef)
            .AddManualTrigger(out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();
        await fixture.PublishWorkflowAsync(otherToken, otherWorkspace, foreignRef, $"E2E-Foreign-{foreignRef:N}", JsonSerializer.SerializeToElement(foreign, JsonOpts));

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var parentRef = Guid.NewGuid();
        var parent = new WorkflowBuilder($"E2E-Parent-{parentRef:N}", parentRef)
            .AddManualTrigger(out _)
            .AddSubWorkflow(foreignRef)
            .BuildAndValidate();

        using var response = await fixture.Management.As(token).PublishWorkflowRawAsync(
            workspaceRef, new WorkflowPublishRequest(parentRef, $"E2E-Parent-{parentRef:N}", null, parent, Enable: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKFLOW_VALIDATION_FAILED");
        (await response.Content.ReadAsStringAsync()).Should().Contain("SUBWORKFLOW_TARGET_NOT_FOUND");
    }
}
