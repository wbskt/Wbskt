using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// "I sent the message and nothing happened" (#416): a trigger's recent outcomes say what it did with
/// each message, and a hold trigger's say it is holding, and until when.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class TriggerActivityE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TRG_ACT_01_A_filtered_message_and_a_started_run_are_both_reported()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("filtered");
        var (workflowRefId, nodeId) = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 0);
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        await client.SendAsync("temperature", new { celsius = 5 });
        await Task.Delay(TimeSpan.FromSeconds(1));
        await client.SendAsync("temperature", new { celsius = 10 });
        Guid runRef = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));

        TriggerActivityResponse activity = await WaitForOutcomesAsync(token, workspaceRef, workflowRefId, nodeId, 2);

        activity.Recent.Select(o => o.Outcome).Should().Equal(["StartedRun", "Filtered"], "newest first: the warm reading started a run, the cool one was filtered out");
        activity.Recent[0].RunRef.Should().Be(runRef);
        activity.Recent.Should().AllSatisfy(o =>
        {
            o.MessageType.Should().Be("temperature");
            o.Channel.Should().Be("client");
        });
        activity.Hold.Should().BeNull("the trigger has no hold time");
    }

    [SkippableFact]
    public async Task TRG_ACT_02_A_hold_trigger_reports_that_it_is_holding()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("holding");
        var (workflowRefId, nodeId) = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 600);
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        await client.SendAsync("temperature", new { celsius = 10 });

        TriggerActivityResponse activity = await WaitForOutcomesAsync(token, workspaceRef, workflowRefId, nodeId, 1);

        activity.Recent.Should().ContainSingle().Which.Outcome.Should().Be("Held");
        activity.Hold.Should().NotBeNull();
        activity.Hold!.State.Should().Be("holding");
        activity.Hold.DueAt.Should().BeCloseTo(activity.Hold.SinceAt!.Value.AddSeconds(600), TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task TRG_ACT_03_Another_workspaces_workflow_is_not_found()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, _, _) = await RegisterDeviceAsync("scoped");
        var (workflowRefId, nodeId) = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 0);
        var (otherToken, otherWorkspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();

        var response = await fixture.SendAsync(HttpMethod.Get, Url(otherWorkspaceRef, workflowRefId, nodeId), otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<TriggerActivityResponse> WaitForOutcomesAsync(string token, Guid workspaceRef, Guid workflowRefId, Guid nodeId, int count)
    {
        TriggerActivityResponse? activity = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var response = await fixture.SendAsync(HttpMethod.Get, Url(workspaceRef, workflowRefId, nodeId), token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            activity = JsonSerializer.Deserialize<TriggerActivityResponse>(await response.Content.ReadAsStringAsync(), JsonOpts);
            if (activity!.Recent.Count >= count)
            {
                return activity;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        activity.Should().NotBeNull();
        activity!.Recent.Should().HaveCountGreaterThanOrEqualTo(count, "each message reaching the trigger is recorded");
        return activity;
    }

    private static string Url(Guid workspaceRef, Guid workflowRefId, Guid nodeId) =>
        ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspaceRef}/workflows/{workflowRefId}/triggers/{nodeId}/recent");

    private async Task<(string Token, Guid WorkspaceRef, Guid ClientRefId, string Secret, string DeviceName)> RegisterDeviceAsync(string label)
    {
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-activity-{label}-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        return (token, workspaceRef, clientRefId, secret, deviceName);
    }

    // A trigger on payload.celsius > 8.
    private async Task<(Guid WorkflowRefId, Guid NodeId)> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, int holdSeconds)
    {
        var workflowRefId = Guid.NewGuid();
        WorkflowExpression filter = new BinaryExpression(new BranchStateRefExpression("payload.celsius"), BinaryOperator.GreaterThan, new LiteralExpression(8));

        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Activity-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), "temperature", WorkflowConcurrencyPolicy.AllowParallel, null, out Guid nodeId, filter: filter, holdSeconds: holdSeconds)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        await fixture.PublishWorkflowAsync(token, workspaceRef, workflowRefId, $"E2E-Activity-{workflowRefId:N}", JsonSerializer.SerializeToElement(definition, JsonOpts));
        return (workflowRefId, nodeId);
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
