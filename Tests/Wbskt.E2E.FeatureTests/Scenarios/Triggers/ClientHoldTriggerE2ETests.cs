using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// A device trigger with a hold time: a reading that stays out of range for the whole hold starts one
/// run, however many more out-of-range readings follow; a reading that drops back in range first
/// starts nothing.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ClientHoldTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task A_reading_that_stays_too_warm_starts_one_run()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("warm");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 2);
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        for (int i = 0; i < 4; i++)
        {
            await client.SendAsync("temperature", new { celsius = 10 });
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "the reading stayed above the limit for the whole hold");

        // Still too warm: the trigger has fired for this hold and stays quiet.
        for (int i = 0; i < 4; i++)
        {
            await client.SendAsync("temperature", new { celsius = 11 });
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        await Task.Delay(TimeSpan.FromSeconds(3));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().HaveCount(1, "a reading that stays out of range alerts once, not on every message");
    }

    [SkippableFact]
    public async Task A_reading_that_recovers_within_the_hold_starts_nothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("recovers");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 10);
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        await client.SendAsync("temperature", new { celsius = 10 });
        await Task.Delay(TimeSpan.FromSeconds(3));
        await client.SendAsync("temperature", new { celsius = 5 });

        // Well past the hold, with the ticker checking every second.
        await Task.Delay(TimeSpan.FromSeconds(15));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().BeEmpty("the reading came back in range before the hold ran out");
    }

    private async Task<(string Token, Guid WorkspaceRef, Guid ClientRefId, string Secret, string DeviceName)> RegisterDeviceAsync(string label)
    {
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-hold-{label}-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        return (token, workspaceRef, clientRefId, secret, deviceName);
    }

    private async Task<Guid> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, int holdSeconds)
    {
        var workflowRefId = Guid.NewGuid();
        // payload.celsius > 8
        WorkflowExpression tooWarm = new BinaryExpression(
            new BranchStateRefExpression("payload.celsius"),
            BinaryOperator.GreaterThan,
            new LiteralExpression(8));

        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Hold-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), "temperature", WorkflowConcurrencyPolicy.AllowParallel, null, out _, filter: tooWarm, holdSeconds: holdSeconds)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        return await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Hold-{workflowRefId:N}",
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
