using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Timeouts;

[Collection(E2ECollection.Name)]
public sealed class BookmarkTtlRaceE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_TimeoutTakenWhenNoSignalReceived()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "Device1");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "Device1",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-TTL-Timeout-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(2), awaitSignal => 
            {
                awaitSignal.OnSuccess(b => b.AddFailRun("Should not have received signal"));
                awaitSignal.OnTimeout(b => b.AddClientMessage(deviceRefStr, "TimeoutFired", "Timeout Fired", null));
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

        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Contains("TimeoutFired"));
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("client must receive TimeoutFired after 2 seconds");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_SignalTakenWhenReceivedImmediately()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "Device2");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "Device2",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-TTL-Signal-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(10), awaitSignal => 
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

        // Wait for the branch to park, then signal it well inside its 10 second TTL. The bookmark is
        // written just after the park is recorded, so the signal is repeated until it matches.
        await fixture.WaitForBranchParkedAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        await fixture.SendSignalUntilMatchedAsync(token, workspaceRef, runRefId, "continue", TimeSpan.FromSeconds(5));

        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Contains("SignalFired"));
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("client must receive SignalFired");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_MassiveConcurrencyRace_ExecutesExactlyOnePath()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "Device3");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "Device3",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-TTL-Race-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(3), awaitSignal => 
            {
                awaitSignal.OnSuccess(b => b.AddClientMessage(deviceRefStr, "SignalFired", "Signal Fired", null));
                awaitSignal.OnTimeout(b => b.AddClientMessage(deviceRefStr, "TimeoutFired", "Timeout Fired", null));
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

        await fixture.WaitForBranchParkedAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));

        // Aim the signals at the 3 second TTL running out. This wait is the scenario, not a wait for
        // something to happen: either side may win, and the assertions below hold for both.
        await Task.Delay(TimeSpan.FromSeconds(2.8));

        // Blast the signal 10 times concurrently
        var tasks = Enumerable.Range(0, 10).Select(_ => fixture.SendSignalAsync(token, workspaceRef, runRefId, "continue"));
        await Task.WhenAll(tasks);

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");

        // The run is finished, so whichever command it sent is on its way; wait for it to land.
        await ServicesFixture.PollUntilAsync(
            "the winning branch's command to arrive",
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult((commands.Count >= 1, $"{commands.Count} command(s)"));
                }
            },
            TimeSpan.FromSeconds(15));

        lock (commandLock)
        {
            commands.Count.Should().Be(1, "exactly one branch should continue, either timeout or signal, but not both");
            commands.First().Should().Match(c => c == "SignalFired" || c == "TimeoutFired");
        }

        // The finished run's history is complete, so it shows a second command a slow socket has not
        // delivered yet: exactly one of the two command nodes ran.
        var commandNodeIds = definition.Nodes.OfType<SendClientMessageNode>().Select(n => n.NodeId).ToHashSet();
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
        history.Count(e => e.EventKind == "NodeCompleted" && e.NodeId is { } id && commandNodeIds.Contains(id))
            .Should().Be(1, "exactly one of the signal and timeout paths runs its command");
    }
}
