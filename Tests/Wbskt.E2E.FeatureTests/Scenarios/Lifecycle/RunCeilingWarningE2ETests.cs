using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// A trigger the workspace's active-run ceiling refuses is thrown away, so the user is told (#432):
/// one Warning in the Events Log per workflow per window, carrying how many were refused.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class RunCeilingWarningE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // WorkflowEngine:MaxActiveRunsPerWorkspace as the hosts run it (the engine's default).
    private const int Ceiling = 200;
    private const int Refused = 5;

    [SkippableFact]
    public async Task RUN_CEIL_01_Triggers_refused_at_the_ceiling_raise_one_warning_with_their_count()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var workflowRefId = Guid.NewGuid();
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Ceiling-{workflowRefId:N}", workflowRefId)
            .AddManualTrigger(out Guid triggerId)
            .AddDelay(TimeSpan.FromMinutes(30))
            .BuildAndValidate();
        await fixture.PublishWorkflowAsync(token, workspaceRef, workflowRefId, $"E2E-Ceiling-{workflowRefId:N}", JsonSerializer.SerializeToElement(definition, JsonOpts));

        try
        {
            int started = 0;
            for (int i = 0; i < Ceiling + Refused; i++)
            {
                var response = await fixture.StartManualRunRawAsync(token, workspaceRef, workflowRefId, triggerId.ToString(), $"ceiling-{i}");
                if (response.IsSuccessStatusCode && (await ServicesFixture.ReadStartRunResponseAsync(response)).Outcome == Wbskt.Management.Models.Workflow.StartRunOutcome.Started)
                {
                    started++;
                }
            }

            started.Should().Be(Ceiling, "every start past the ceiling is refused");

            EventLogResponse warning = await WaitForWarningAsync(token, workspaceRef);

            warning.Criticality.Should().Be(EventCriticality.Warning);
            warning.WorkflowRefId.Should().Be(workflowRefId);
            using var data = JsonDocument.Parse(warning.EventData);
            data.RootElement.GetProperty("refusedCount").GetInt32().Should().Be(Refused);
            data.RootElement.GetProperty("ceiling").GetInt32().Should().Be(Ceiling);
            data.RootElement.GetProperty("triggerKinds").GetString().Should().Be("manual");
        }
        finally
        {
            // Deleting the workflow cancels its runs, so the workspace does not hold 200 for half an hour.
            await fixture.Management.As(token).DeleteWorkflowRawAsync(workspaceRef, workflowRefId);
        }
    }

    private async Task<EventLogResponse> WaitForWarningAsync(string token, Guid workspaceRef)
    {
        // The warning goes out once its window (a minute from the first refusal) is over.
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            var warnings = await fixture.GetEventLogsAsync(token, workspaceRef, "WorkflowTriggerRefusedEvent");
            if (warnings.Count > 0)
            {
                warnings.Should().ContainSingle("a burst of refusals is one warning, not one per refusal");
                return warnings[0];
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException("No WorkflowTriggerRefusedEvent reached the Events Log.");
    }
}
