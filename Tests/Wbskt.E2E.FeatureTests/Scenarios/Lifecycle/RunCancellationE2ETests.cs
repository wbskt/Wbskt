using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

[Collection(E2ECollection.Name)]
public sealed class RunCancellationE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task CancelRun_TransitionsToCancelled_AndDeletesBookmarks()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceCancellation1");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceCancellation1",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-RunCancel-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(60), awaitSignal => 
            {
                awaitSignal.OnSuccess(b => b.AddClientMessage(deviceRefStr, "SignalFired", "Signal Fired", null));
                awaitSignal.OnTimeout(b => b.AddFailRun("Should not have timed out"));
            });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // Run is now waiting at the AwaitSignal bookmark.
        // We cancel the run.
        await fixture.CancelRunAsync(token, workspaceRef, runRefId);

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Cancelled");

        // Try to send the signal; it should fail or not trigger anything
        var (matched, _) = await fixture.SendSignalAsync(token, workspaceRef, runRefId, "continue");
        matched.Should().BeFalse("because the run was cancelled and the signal bookmark should have been deleted");

        // Wait a bit to ensure no command is received
        await Task.Delay(2000);

        lock (commandLock)
        {
            commands.Should().BeEmpty("because the run was cancelled and the signal should not resume it");
        }
    }
}
