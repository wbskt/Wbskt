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

        // Fire the webhook via Management's public callback (retry in case registration is delayed).
        (string Outcome, Guid? RunRefId) webhookResp = default;

        await ServicesFixture.PollAsync(async () =>
        {
            webhookResp = await fixture.SendWebhookAsync(path, new { test = "payload" });
            return webhookResp.Outcome == "StartedRun";
        }, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        // 4. Assert the run succeeds
        webhookResp.Outcome.Should().Be("StartedRun");
        webhookResp.RunRefId.Should().NotBeNull();

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, webhookResp.RunRefId!.Value, TimeSpan.FromSeconds(30));
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
