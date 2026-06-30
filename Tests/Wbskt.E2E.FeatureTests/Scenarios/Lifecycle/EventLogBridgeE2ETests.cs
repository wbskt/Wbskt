using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// History → Events Log bridge — proves run lifecycle milestones published by the Engine Host are
/// consumed by the Management Host's EventLoggerHandler and surface in the central event-log feed,
/// scoped to their workflow (the previously-unused WorkflowRefId column is now populated).
///
/// Topology: DeviceTrigger ─► End  (a trivially-succeeding run).
///
/// After the run reaches Succeeded we poll GET /event-logs and assert both a WorkflowRunStartedEvent
/// and a WorkflowRunCompletedEvent appear with this workflow's RefId, and that the completed event
/// is Info criticality (0). The detailed per-node trace remains in the separate run-history API.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class EventLogBridgeE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const int InfoCriticality = 0;

    [SkippableFact]
    public async Task SucceededRun_SurfacesStartedAndCompletedMilestones_InEventLog()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-eventlog-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildTrivialDefinition(workflowRefId, clientRefId.ToString());
        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-EventLog-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "eventlog-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started a run");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded");

        // The milestones flow async through the bus → buffer → batch flush, so poll the event-log.
        EventLogItemDto? completed = null;
        EventLogItemDto? started = null;
        var surfaced = await ServicesFixture.PollAsync(
            async () =>
            {
                var logs = await fixture.GetEventLogsAsync(token, workspaceRef);
                started ??= logs.FirstOrDefault(e =>
                    e.EventName == "WorkflowRunStartedEvent" && e.WorkflowRefId == publishedRef);
                completed ??= logs.FirstOrDefault(e =>
                    e.EventName == "WorkflowRunCompletedEvent" && e.WorkflowRefId == publishedRef);
                return started is not null && completed is not null;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(2));

        surfaced.Should().BeTrue(
            "both the started and completed run milestones must surface in the central event-log scoped to this workflow");
        completed!.Criticality.Should().Be(InfoCriticality, "a successful run is an informational milestone");
        completed.EventData.Should().Contain("Succeeded", "the completed event payload carries the terminal status");
    }

    private static WorkflowDefinition BuildTrivialDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-EventLog-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddEnd();

        return builder.BuildAndValidate();
    }
}
