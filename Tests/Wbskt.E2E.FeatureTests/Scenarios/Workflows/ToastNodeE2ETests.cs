using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Workflows;

/// <summary>
/// <c>action:toast</c> raises a toast to the workspace's dashboard connections. Its only durable trace
/// is the <c>WorkflowToastEvent</c> it publishes, which the event log keeps, so that is what this
/// asserts beside the node completing and the run succeeding.
///
/// Requires the auth, management and engine hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class ToastNodeE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task A_toast_node_completes_and_its_toast_reaches_the_event_log()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();

        var workflowRef = Guid.NewGuid();
        string title = $"e2e-toast-{Guid.NewGuid():N}";
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Toast-{workflowRef:N}", workflowRef)
            .AddManualTrigger(out var triggerId)
            .AddToast(title, "raised by the toast scenario")
            .BuildAndValidate();
        Guid toastNodeId = definition.Nodes.OfType<ToastNotificationNode>().Single().NodeId;

        var published = await fixture.PublishWorkflowAsync(
            token, workspace, workflowRef, $"E2E-Toast-{workflowRef:N}", JsonSerializer.SerializeToElement(definition, JsonOpts));

        var runRef = await fixture.WaitForManualTriggerLiveAsync(token, workspace, published, triggerId, Timeout);
        (await fixture.WaitForRunTerminalAsync(token, workspace, runRef, Timeout))!.Status.Should().Be("Succeeded");

        var history = await fixture.GetHistoryAsync(token, workspace, runRef);
        history.Should().Contain(e => e.EventKind == "NodeCompleted" && e.NodeId == toastNodeId, "the toast node ran");

        // The event log is written in batches off the bus, so poll for it.
        var toast = await ServicesFixture.PollUntilAsync<EventLogResponse>(
            $"a WorkflowToastEvent for workflow {published}",
            async () =>
            {
                var logs = await fixture.GetEventLogsAsync(token, workspace, "WorkflowToastEvent");
                var found = logs.FirstOrDefault(e => e.WorkflowRefId == published);
                return (found is not null, found!, $"{logs.Count} toast entries");
            },
            Timeout,
            TimeSpan.FromSeconds(2));

        toast.EventData.Should().Contain(title);
    }
}
