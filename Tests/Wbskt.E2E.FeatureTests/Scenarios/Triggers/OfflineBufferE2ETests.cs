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
/// A device that sends while offline keeps the reading and delivers it on connect, with the time it
/// was sent, so a workflow can tell a late reading from a live one.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class OfflineBufferE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task A_reading_sent_while_offline_arrives_on_connect_marked_late()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-offline-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, late: true);
        // Its opposite, on the same device and message type: it starts a run for a live reading only.
        // A client message is matched against every trigger watching it in one pass, so once this one
        // has run for the live reading below, the late-only trigger has judged that reading too.
        var liveOnlyRefId = await PublishAsync(token, workspaceRef, clientRefId, late: false);

        var storage = new InMemoryClientStorage(clientRefId, secret);
        await using var client = new WbsktClient(new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null), storage);

        // Not connected yet: the reading waits in the buffer instead of throwing.
        await client.SendAsync("temperature", new { celsius = 12 });
        client.PendingMessageCount.Should().Be(1);

        // Past the 30 second late threshold. This wait is the scenario: the reading has to be that old
        // when it arrives, and nothing can be observed while the device is offline.
        await Task.Delay(TimeSpan.FromSeconds(32));
        await client.StartAsync();

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "the buffered reading was delivered on connect and marked late");
        client.PendingMessageCount.Should().Be(0);

        // A live reading is not late, so the late-only trigger stays quiet. The live-only trigger
        // running for it is what shows the reading has been judged.
        await client.SendAsync("temperature", new { celsius = 13 });
        await fixture.WaitForFirstRunAsync(token, workspaceRef, liveOnlyRefId, TimeSpan.FromSeconds(60));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().HaveCount(1, "only the reading that waited offline is late");
        (await fixture.ListRunsAsync(token, workspaceRef, liveOnlyRefId)).Should().HaveCount(1, "only the reading sent while connected is live");
    }

    private async Task<Guid> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, bool late)
    {
        var workflowRefId = Guid.NewGuid();
        // late == true, or late == false
        WorkflowExpression filter = new BinaryExpression(
            new BranchStateRefExpression("late"),
            BinaryOperator.Equal,
            new LiteralExpression(late));

        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Offline-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), "temperature", WorkflowConcurrencyPolicy.AllowParallel, null, out _, filter: filter)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        return await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Offline-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));
    }
}
