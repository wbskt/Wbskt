using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

[Collection(E2ECollection.Name)]
public sealed class TriggerConcurrencyE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task DropIfRunning_DropsConcurrentEvents()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceConcurrency1");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceConcurrency1",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-Concurrency-DropIfRunning-{workflowRefId:N}", workflowRefId)
            // Use DropIfRunning policy with correlation based on the device ref
            .AddDeviceTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.DropIfRunning, "trigger.clientRefId", out var triggerNodeId);

        // Add an AwaitSignal so the run stays Active. We don't care if it times out or signals.
        builder.SetHead(triggerNodeId, "default")
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(5), out var awaitNodeId);

        builder.SetHead(awaitNodeId, "default")
            .AddFailRun("Should not have received signal");

        builder.SetHead(awaitNodeId, "timeout")
            .AddSendCommand(deviceRefStr, "TimeoutFired", "Timeout Fired", null);

        var definition = builder.Build();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // ── 3. Fire 5 telemetry events ──────────────────────
        foreach (int i in Enumerable.Range(0, 5))
        {
            await wbsktClient.SendTelemetryAsync("telemetry", new { });
            await Task.Delay(50);
        }

        // Wait a little bit for processing
        await Task.Delay(2000);

        // ── 4. Assert that exactly 1 run was created ──────────────────────
        var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
        
        runs.Should().HaveCount(1, "because DropIfRunning should drop all concurrent triggers with the same correlation key");
    }
}
