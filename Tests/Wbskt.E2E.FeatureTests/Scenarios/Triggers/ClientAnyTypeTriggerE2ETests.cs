using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// A device trigger on any message type starts on the device's own messages, not on the platform's
/// (state.report, capabilities); a trigger that names a platform type still starts on it.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class ClientAnyTypeTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Any_message_type_skips_state_reports_but_a_trigger_naming_them_runs()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-any-type-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var anyRefId = await PublishAsync(token, workspaceRef, clientRefId, "*");
        var stateRefId = await PublishAsync(token, workspaceRef, clientRefId, "state.report");

        var storage = new InMemoryClientStorage(clientRefId, secret);
        await using var client = new WbsktClient(new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null), storage);
        await client.StartAsync();

        // The trigger that names state.report runs, so the report has been dispatched to every trigger
        // on this device before the device's own message is sent.
        await client.ReportStateAsync(new Dictionary<string, object?> { ["celsius"] = 4 });
        await fixture.WaitForFirstRunAsync(token, workspaceRef, stateRefId, TimeSpan.FromSeconds(60));

        await client.SendAsync("reading", new { celsius = 4 });
        await fixture.WaitForFirstRunAsync(token, workspaceRef, anyRefId, TimeSpan.FromSeconds(60));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, anyRefId);
        runs.Should().HaveCount(1, "only the reading is the device's own message; the state report is the platform's");
    }

    private async Task<Guid> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, string type)
    {
        var workflowRefId = Guid.NewGuid();
        var name = $"E2E-AnyType-{workflowRefId:N}";

        WorkflowDefinition definition = new WorkflowBuilder(name, workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), type, WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        return await fixture.PublishWorkflowAsync(token, workspaceRef, workflowRefId, name, JsonSerializer.SerializeToElement(definition, JsonOpts));
    }
}
