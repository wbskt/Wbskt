using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

[Collection(E2ECollection.Name)]
public sealed class WebhookTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task WebhookTrigger_StartsRunFromInboundHttpRequest()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-webhook-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var path = $"test-hook-{Guid.NewGuid():N}";
        
        var definition = BuildWebhookDefinition(workflowRefId, path);

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Webhook-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // Give the client time to connect
        await Task.Delay(500);

        // Fire the workspace-scoped webhook via Management's public callback. The callback is opaque
        // (202), so we retry firing until a run appears (registration may be momentarily delayed) and
        // observe the started run via the runs API rather than the response body.
        Guid runRefId = Guid.Empty;
        var runStarted = await ServicesFixture.PollAsync(async () =>
        {
            await fixture.SendWebhookAsync(workspaceRef, path, new { test = "payload" });
            var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
            if (runs.Count > 0) { runRefId = runs[0].RefId; return true; }
            return false;
        }, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));

        // 4. Assert the run succeeds
        runStarted.Should().BeTrue("the webhook callback should have started a run");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    private static WorkflowDefinition BuildWebhookDefinition(Guid workflowRefId, string path)
    {
        var builder = new WorkflowBuilder($"E2E-Webhook-{workflowRefId:N}", workflowRefId)
            .AddWebhookTrigger(path, "POST", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10));

        // We only assert the run terminal state. Sending a message would fail because webhooks don't have a targeted clientRef payload.

        return builder.BuildAndValidate();
    }
}
