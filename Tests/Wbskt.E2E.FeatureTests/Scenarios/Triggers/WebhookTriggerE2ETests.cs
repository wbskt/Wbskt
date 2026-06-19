using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

public sealed record InboundWebhookResponse(string Outcome, Guid? RunId);

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

        // Give the device time to connect
        await Task.Delay(500);

        // Send HTTP POST to the webhook endpoint (retry in case registration is delayed)
        using var http = new HttpClient();
        InboundWebhookResponse? webhookResp = null;
        
        await ServicesFixture.PollAsync(async () =>
        {
            var resp = await http.PostAsJsonAsync($"{E2EConfig.WorkflowBaseUrl}/api/inbound/webhook/{path}", new { test = "payload" });
            resp.EnsureSuccessStatusCode();
            webhookResp = await resp.Content.ReadFromJsonAsync<InboundWebhookResponse>(JsonOpts);
            return webhookResp?.Outcome == "StartedRun";
        }, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        
        // 4. Assert the run succeeds
        webhookResp.Should().NotBeNull();
        webhookResp!.Outcome.Should().Be("StartedRun");
        webhookResp.RunId.Should().NotBeNull();
        
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, webhookResp.RunId!.Value, TimeSpan.FromSeconds(30));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    private static WorkflowDefinition BuildWebhookDefinition(Guid workflowRefId, string path)
    {
        var builder = new WorkflowBuilder($"E2E-Webhook-{workflowRefId:N}", workflowRefId)
            .AddWebhookTrigger(path, "POST", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10));

        // We only assert the run terminal state. Sending a command would fail because webhooks don't have a targeted deviceRef payload.

        return builder.BuildAndValidate();
    }
}
