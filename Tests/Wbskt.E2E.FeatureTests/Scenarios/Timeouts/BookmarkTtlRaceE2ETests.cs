using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.E2E.FeatureTests.Scenarios.Timeouts;

[Collection(E2ECollection.Name)]
public sealed class BookmarkTtlRaceE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_TimeoutTakenWhenNoSignalReceived()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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
            .AddDeviceTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId);

        builder.SetHead(triggerNodeId, "default")
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(2), out var awaitNodeId);

        builder.SetHead(awaitNodeId, "default")
            .AddFailRun("Should not have received signal");

        builder.SetHead(awaitNodeId, "timeout")
            .AddSendCommand(deviceRefStr, "TimeoutFired", "Timeout Fired", null);

        var definition = builder.Build();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendTelemetryAsync("start", new { });

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
            
        arrived.Should().BeTrue("device must receive TimeoutFired after 2 seconds");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_SignalTakenWhenReceivedImmediately()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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
            .AddDeviceTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId);

        builder.SetHead(triggerNodeId, "default")
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(10), out var awaitNodeId);

        builder.SetHead(awaitNodeId, "default")
            .AddSendCommand(deviceRefStr, "SignalFired", "Signal Fired", null);

        builder.SetHead(awaitNodeId, "timeout")
            .AddFailRun("Should not have timed out");

        var definition = builder.Build();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendTelemetryAsync("start", new { });
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // Send the signal immediately
        await fixture.SendSignalAsync(token, workspaceRef, runRefId, "continue");

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
            
        arrived.Should().BeTrue("device must receive SignalFired");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task AwaitSignal_WithTtl_MassiveConcurrencyRace_ExecutesExactlyOnePath()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
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
            .AddDeviceTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId);

        // Very short TTL to force a race condition
        builder.SetHead(triggerNodeId, "default")
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(3), out var awaitNodeId);

        builder.SetHead(awaitNodeId, "default")
            .AddSendCommand(deviceRefStr, "SignalFired", "Signal Fired", null);

        builder.SetHead(awaitNodeId, "timeout")
            .AddSendCommand(deviceRefStr, "TimeoutFired", "Timeout Fired", null);

        var definition = builder.Build();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendTelemetryAsync("start", new { });
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // Wait 2.8 seconds, then blast the signal
        await Task.Delay(TimeSpan.FromSeconds(2.8));

        // Blast the signal 10 times concurrently
        var tasks = Enumerable.Range(0, 10).Select(_ => fixture.SendSignalAsync(token, workspaceRef, runRefId, "continue"));
        await Task.WhenAll(tasks);

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");

        lock (commandLock)
        {
            commands.Count.Should().Be(1, "exactly one branch should continue, either timeout or signal, but not both");
            commands.First().Should().Match(c => c == "SignalFired" || c == "TimeoutFired");
        }
    }
}
