using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

[Collection(E2ECollection.Name)]
public sealed class TriggerConcurrencyE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task DropIfRunning_DropsConcurrentEvents()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
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
            // Use DropIfRunning policy with correlation based on the client ref
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.DropIfRunning, "trigger.clientRefId", out var triggerNodeId)
            // Add an AwaitSignal so the run stays Active. We don't care if it times out or signals.
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(5), awaitSignal => 
            {
                awaitSignal.OnSuccess(b => b.AddFailRun("Should not have received signal"));
                awaitSignal.OnTimeout(b => b.AddClientMessage(deviceRefStr, "TimeoutFired", "Timeout Fired", null));
            });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // A sentinel on the same device and message type that runs for every event. A client message
        // is matched against every trigger watching it in one pass, so once the sentinel has a run
        // per event, the trigger under test has been offered all five as well.
        var sentinelRefId = Guid.NewGuid();
        var sentinel = new WorkflowBuilder($"E2E-Concurrency-Sentinel-{sentinelRefId:N}", sentinelRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .BuildAndValidate();
        var sentinelPublishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, sentinelRefId, sentinel.Name,
            JsonSerializer.SerializeToElement(sentinel, JsonOpts));

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // ── 3. Fire 5 telemetry events ──────────────────────
        foreach (int i in Enumerable.Range(0, 5))
        {
            await wbsktClient.SendAsync("telemetry", new { });
            await Task.Delay(50);
        }

        await fixture.WaitForRunCountAsync(token, workspaceRef, sentinelPublishedRef, 5, TimeSpan.FromSeconds(30));

        // ── 4. Assert that exactly 1 run was created ──────────────────────
        var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
        
        runs.Should().HaveCount(1, "because DropIfRunning should drop all concurrent triggers with the same correlation key");
    }
}
