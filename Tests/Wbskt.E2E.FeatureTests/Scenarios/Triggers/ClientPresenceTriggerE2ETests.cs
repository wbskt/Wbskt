using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// A device going offline (or coming online) and staying that way for the trigger's grace period
/// starts a run; a device that comes back within it does not.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ClientPresenceTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Offline_trigger_runs_once_the_device_has_stayed_offline()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("offline");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, ClientPresenceState.Offline, forSeconds: 2);

        var client = await ConnectAsync(clientRefId, secret, deviceName);
        await client.DisposeAsync();

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "the device stayed offline past the grace period");
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary!.Status.Should().Be("Succeeded");
    }

    [SkippableFact]
    public async Task Offline_trigger_does_not_run_when_the_device_comes_back_within_the_grace_period()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("blip");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, ClientPresenceState.Offline, forSeconds: 15);

        var first = await ConnectAsync(clientRefId, secret, deviceName);
        await first.DisposeAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await using var second = await ConnectAsync(clientRefId, secret, deviceName);

        // Well past the grace period, with the ticker checking every second.
        await Task.Delay(TimeSpan.FromSeconds(25));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().BeEmpty("the device was back online before the grace period ran out");
    }

    [SkippableFact]
    public async Task Online_trigger_runs_when_the_device_connects()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("online");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, ClientPresenceState.Online, forSeconds: 0);

        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "the device came online");
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary!.Status.Should().Be("Succeeded");
    }

    private async Task<(string Token, Guid WorkspaceRef, Guid ClientRefId, string Secret, string DeviceName)> RegisterDeviceAsync(string label)
    {
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-presence-{label}-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        return (token, workspaceRef, clientRefId, secret, deviceName);
    }

    private async Task<Guid> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, ClientPresenceState state, int forSeconds)
    {
        var workflowRefId = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Presence-{workflowRefId:N}", workflowRefId)
            .AddClientPresenceTrigger(clientRefId.ToString(), state, forSeconds, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        return await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Presence-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));
    }

    private static async Task<WbsktClient> ConnectAsync(Guid clientRefId, string secret, string deviceName)
    {
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        var client = new WbsktClient(clientConfig, storage);
        await client.StartAsync();
        return client;
    }
}
